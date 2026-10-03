using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spot.Framework;
using Spot.Framework.IO;

namespace Spot.Engine.Scenes;

/// <summary>
/// Reads and writes a <see cref="Scene"/> as <c>.sptscene</c> JSON. Component data is handled entirely
/// by reflection through <see cref="ComponentSerialization"/> — every component tagged with
/// <see cref="SceneComponentAttribute"/> is written and read automatically, so adding a serializable
/// component needs no changes here. The structural pieces are handled explicitly: the
/// <see cref="LabelComponent"/> (it carries the entity name and drives <see cref="Scene.Instantiate"/>),
/// the user components (written in order under <c>"Components"</c> by guid and class name with their public
/// fields, and resolved and instantiated at load — an unresolvable one is kept verbatim in
/// <see cref="MissingComponents"/>), and the legacy <see cref="ScriptComponent"/>. The reader tolerates missing files, empty input, a UTF-8 BOM, malformed JSON, unknown
/// component keys, and missing assets — logging and continuing rather than throwing.
/// </summary>
public class SceneSerializer
{
    private readonly Scene _scene;

    public SceneSerializer(Scene scene)
    {
        _scene = scene;
    }

    public string SerializeToString()
    {
        // Give every entity a stable id before writing so an Entity-typed script field can reference a target
        // that is written later in the file — the reference stores the target's id, which must already exist.
        foreach (Entity entity in _scene.View<LabelComponent>())
        {
            entity.EnsurePersistentId();
        }

        var entities = new JsonArray();
        foreach (var entity in _scene.View<LabelComponent>())
        {
            if (entity.Parent == null)
            {
                entities.Add(WriteEntity(entity));
            }
        }

        var root = new JsonObject { ["Entities"] = entities };
        return root.ToJsonString(SceneJson.WriteOptions);
    }

    /// <summary>
    /// Writes a single entity and its descendants to a JSON object using the same reflection-based component
    /// handling as a full scene. Shared with the prefab serializer, which stores one such subtree.
    /// </summary>
    /// <summary>
    /// Assigns a stable id to <paramref name="root"/> and every descendant, so that a subtree (a prefab) can
    /// be written with all entity references pointing at targets that already have ids.
    /// </summary>
    internal static void EnsureSubtreeIds(Entity root)
    {
        root.EnsurePersistentId();
        foreach (Entity child in root.Children)
        {
            EnsureSubtreeIds(child);
        }
    }

    internal static JsonObject WriteEntity(Entity entity)
    {
        var obj = new JsonObject();

        // Tag is structural: it holds the name, the entity's enabled state, its category tag, and its stable
        // id (so entity references survive renames and reordering).
        var tag = entity.GetComponent<LabelComponent>();
        var tagObj = new JsonObject { ["Name"] = tag.Name, ["Enabled"] = entity.Enabled };
        if (!string.IsNullOrEmpty(tag.Tag))
        {
            tagObj["Tag"] = tag.Tag;
        }

        string id = entity.EnsurePersistentId();
        tagObj["Id"] = id;

        obj["Tag"] = tagObj;

        // Every registered component is written generically by reflection.
        foreach (var (type, key) in ComponentSerialization.WriteOrder)
        {
            if (entity.HasComponent(type))
            {
                obj[key] = ComponentSerialization.Serialize(entity.GetComponent(type)!);
            }
        }

        // User components are written in the entity's order, followed by any that could not be resolved
        // (kept verbatim so a missing script never loses its data).
        JsonArray userComponents = WriteUserComponents(entity);
        if (userComponents.Count > 0)
        {
            obj["Components"] = userComponents;
        }

        // Scripts are special: only the class names are stored (runtime instances are rebuilt on load).
        if (entity.TryGetComponent(out ScriptComponent? scripts))
        {
            obj["Scripts"] = SerializeScripts(scripts);
        }

        var children = entity.Children.ToList();
        if (children.Count > 0)
        {
            var childArray = new JsonArray();
            foreach (var child in children)
            {
                childArray.Add(WriteEntity(child));
            }
            obj["Children"] = childArray;
        }

        return obj;
    }

    private static JsonArray WriteUserComponents(Entity entity)
    {
        var items = new JsonArray();
        foreach (Component component in entity.Components)
        {
            if (!component.IsUserComponent)
            {
                continue;
            }

            items.Add(WriteUserComponent(component));
        }

        if (entity.TryGetComponent(out MissingComponents? missing))
        {
            foreach (MissingComponent item in missing.Items)
            {
                items.Add(item.Data.DeepClone());
            }
        }

        return items;
    }

    private static JsonObject WriteUserComponent(Component component)
    {
        Type type = component.GetType();
        var entry = new JsonObject { ["Type"] = type.Name };
        if (ScriptRegistry.TryGetByType(type, out ScriptDescriptor? descriptor) && !string.IsNullOrEmpty(descriptor!.Guid))
        {
            entry["Guid"] = descriptor.Guid;
        }

        entry["Enabled"] = component.Enabled;
        JsonObject fields = ComponentSerialization.SerializeMembers(component);
        if (fields.Count > 0)
        {
            entry["Fields"] = fields;
        }

        return entry;
    }

    private static JsonObject SerializeScripts(ScriptComponent scripts)
    {
        var items = new JsonArray();
        foreach (ScriptInstance item in scripts.Items)
        {
            var entry = new JsonObject { ["Type"] = item.ClassName };

            // Persist the stable guid as the primary reference so a class rename never breaks the scene. If
            // the entry has none yet but the registry knows the resolved type, capture it now (this upgrades
            // pre-guid scenes to a stable reference on their next save).
            string guid = item.Guid;
            if (string.IsNullOrEmpty(guid) && item.Instance is not null
                && ScriptRegistry.TryGetByName(item.ClassName, out ScriptDescriptor? descriptor))
            {
                guid = descriptor!.Guid;
            }

            if (!string.IsNullOrEmpty(guid))
            {
                entry["Guid"] = guid;
            }

            // Persist the script's authored field values when the instance is available. Unresolved
            // scripts (no instance) keep just their class name so the reference survives.
            if (item.Instance is not null)
            {
                JsonObject fields = ComponentSerialization.SerializeMembers(item.Instance);
                if (fields.Count > 0)
                {
                    entry["Fields"] = fields;
                }
            }

            items.Add(entry);
        }

        return new JsonObject { ["Enabled"] = scripts.Enabled, ["Items"] = items };
    }

    public void Serialize(string filepath)
    {
        try
        {
            File.WriteAllText(filepath, SerializeToString());
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to save scene '{0}': {1}", filepath, ex.Message);
        }
    }

    public bool DeserializeFromString(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Log.CoreError("Failed to load scene: the file is empty.");
            return false;
        }

        // Some editors save JSON with a leading UTF-8 BOM; strip it so the parser doesn't choke.
        json = json.TrimStart('﻿');

        JsonNode? root;
        try
        {
            root = SceneJson.Parse(json);
        }
        catch (JsonException ex)
        {
            Log.CoreError("Failed to parse scene: {0}", ex.Message);
            return false;
        }

        if (root is not JsonObject rootObj)
        {
            return false;
        }

        // A scene with no "Entities" array is valid (an empty scene) rather than an error. All top-level
        // entities share one reference context so an Entity field can point across the whole scene; the
        // deferred bindings are resolved once every entity exists.
        if (rootObj["Entities"] is JsonArray entities)
        {
            var refs = new SceneReferences();
            foreach (JsonNode? entityNode in entities)
            {
                if (entityNode is JsonObject entityObj)
                {
                    ReadEntitySubtree(_scene, entityObj, null, refs, freshIds: false);
                }
            }

            refs.ResolveDeferred();
        }

        return true;
    }

    /// <summary>
    /// Reads a single entity (and its descendants) from a JSON object into the given scene under an optional
    /// parent, returning the created entity. Shared with the prefab serializer, which instantiates one such
    /// subtree — its entity ids are regenerated so repeated instances never collide, while references inside
    /// the subtree are remapped to the fresh instance. Component and script failures are logged and skipped.
    /// </summary>
    internal static Entity ReadEntity(Scene scene, JsonObject entityObj, Entity? parent) =>
        ReadEntity(scene, entityObj, parent, preserveIds: false);

    /// <summary>
    /// Reads a single entity subtree, optionally keeping the stable ids stored in the JSON instead of
    /// minting fresh ones. Prefab instancing wants fresh ids so repeated instances never collide; the
    /// editor's undo wants the originals preserved, because restoring a deleted entity under a new
    /// identity would orphan every history entry and every <see cref="Entity"/>-typed field that
    /// referenced it.
    /// </summary>
    /// <param name="scene">The scene to read into.</param>
    /// <param name="entityObj">The entity's JSON object.</param>
    /// <param name="parent">The parent to attach under, or <see langword="null"/> for a root entity.</param>
    /// <param name="preserveIds">Keep the stored stable ids rather than regenerating them.</param>
    internal static Entity ReadEntity(Scene scene, JsonObject entityObj, Entity? parent, bool preserveIds)
    {
        var refs = new SceneReferences();
        Entity entity = ReadEntitySubtree(scene, entityObj, parent, refs, freshIds: !preserveIds);
        refs.ResolveDeferred();
        return entity;
    }

    /// <summary>
    /// Reads one entity subtree, registering ids and queuing entity references on <paramref name="refs"/>
    /// without resolving them (the caller resolves once the whole load finishes). When
    /// <paramref name="freshIds"/> is set, the entity is given a new id (prefab instancing) while still being
    /// registered under its stored id so internal references remap to it; otherwise its stored id is kept.
    /// </summary>
    private static Entity ReadEntitySubtree(
        Scene scene, JsonObject entityObj, Entity? parent, SceneReferences refs, bool freshIds)
    {
        string name = "Entity";
        bool enabled = true;
        string tag = string.Empty;
        string storedId = string.Empty;
        if (entityObj["Tag"] is JsonObject tagObj)
        {
            name = tagObj["Name"]?.GetValue<string>() ?? "Entity";
            enabled = tagObj["Enabled"]?.GetValue<bool>() ?? true;
            tag = tagObj["Tag"]?.GetValue<string>() ?? string.Empty;
            storedId = tagObj["Id"]?.GetValue<string>() ?? string.Empty;
        }

        var entity = scene.Instantiate(name);
        entity.Enabled = enabled;
        entity.Tag = tag;
        if (parent != null)
        {
            entity.SetParent(parent);
        }

        // Keep the stored id for a normal load (references stay stable across saves); allocate a fresh one for
        // a prefab instance so two instances don't share ids. Either way register under the stored id so
        // references authored inside this subtree resolve to this entity.
        entity.PersistentId = freshIds || string.IsNullOrEmpty(storedId)
            ? entity.EnsurePersistentId()
            : storedId;
        refs.Register(string.IsNullOrEmpty(storedId) ? entity.PersistentId : storedId, entity);

        foreach (var (key, node) in entityObj)
        {
            if (key == "Components" && node is JsonArray userComponents)
            {
                ReadUserComponents(entity, userComponents, refs);
                continue;
            }

            if (key is "Tag" or "Children" || node is not JsonObject componentObj)
            {
                continue;
            }

            if (key == "Scripts")
            {
                DeserializeScripts(entity, componentObj, refs);
                continue;
            }

            if (ComponentSerialization.TryResolveKey(key, out Type? type))
            {
                try
                {
                    entity.AddComponent(ComponentSerialization.Deserialize(type, componentObj));
                }
                catch (Exception ex)
                {
                    Log.CoreError("Failed to load component '{0}': {1}", key, ex.Message);
                }
            }
            else
            {
                Log.CoreWarn("Unknown component '{0}' in scene; skipping.", key);
            }
        }

        if (entityObj["Children"] is JsonArray children)
        {
            foreach (JsonNode? childNode in children)
            {
                if (childNode is JsonObject childObj)
                {
                    ReadEntitySubtree(scene, childObj, entity, refs, freshIds);
                }
            }
        }

        return entity;
    }

    private static void DeserializeScripts(Entity entity, JsonObject data, SceneReferences refs)
    {
        var scripts = new ScriptComponent { Enabled = data["Enabled"]?.GetValue<bool>() ?? true };
        entity.AddComponent(scripts);

        if (data["Items"] is JsonArray items)
        {
            // New format: each entry carries the class name and its serialized field values.
            foreach (JsonNode? node in items)
            {
                if (node is not JsonObject entry)
                {
                    continue;
                }

                string? className = entry["Type"]?.GetValue<string>();
                string? guid = entry["Guid"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(className) || !string.IsNullOrEmpty(guid))
                {
                    AddScript(entity, scripts, guid, className ?? string.Empty, entry["Fields"] as JsonObject, refs);
                }
            }
        }
        else if (data["ScriptNames"] is JsonArray names)
        {
            // Legacy format: a plain array of class names (no persisted field values).
            foreach (JsonNode? node in names)
            {
                string? className = node?.GetValue<string>();
                if (!string.IsNullOrEmpty(className))
                {
                    AddScript(entity, scripts, null, className, null, refs);
                }
            }
        }
    }

    private static void ReadUserComponents(Entity entity, JsonArray items, SceneReferences refs)
    {
        foreach (JsonNode? node in items)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            if (!TryCreateUserComponent(entity, entry, refs))
            {
                if (!entity.TryGetComponent(out MissingComponents? missing))
                {
                    missing = entity.AddComponent(new MissingComponents());
                }

                missing.Items.Add(new MissingComponent((JsonObject)entry.DeepClone()));
            }
        }
    }

    // Instantiates the user component an entry names, restores its enabled state and fields, and attaches it.
    // Returns false (attaching nothing) when the type cannot be resolved or construction fails.
    private static bool TryCreateUserComponent(Entity entity, JsonObject entry, SceneReferences refs)
    {
        string className = ReadString(entry, "Type");
        string guid = ReadString(entry, "Guid");
        Component? component = ScriptResolver.CreateComponent(guid, className);
        if (component is null)
        {
            return false;
        }

        try
        {
            if (entry["Enabled"] is JsonValue enabled && enabled.TryGetValue(out bool isEnabled))
            {
                component.Enabled = isEnabled;
            }

            if (entry["Fields"] is JsonObject fields)
            {
                ComponentSerialization.ApplyMembers(component, fields, refs);
            }

            entity.AddComponent(component);
            return true;
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to load component '{0}': {1}", className, ex.Message);
            return false;
        }
    }

    private static string ReadString(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue(out string? text) ? text : string.Empty;

    /// <summary>
    /// Retries every entry in the scene's <see cref="MissingComponents"/> — for example once the editor has
    /// loaded the game's scripts — attaching each one whose type now resolves (with its fields and entity
    /// references restored) and keeping the rest. An entity left with no missing entries loses the holder.
    /// </summary>
    /// <param name="scene">The scene to resolve.</param>
    /// <returns>The number of components resolved.</returns>
    internal static int ResolveMissingComponents(Scene scene)
    {
        IReadOnlyList<Entity> holders = scene.View<MissingComponents>();
        if (holders.Count == 0)
        {
            return 0;
        }

        var refs = new SceneReferences();
        foreach (Entity entity in scene.View<LabelComponent>())
        {
            refs.Register(entity.EnsurePersistentId(), entity);
        }

        int resolved = 0;
        foreach (Entity entity in holders)
        {
            MissingComponents missing = entity.GetComponent<MissingComponents>();
            resolved += missing.Items.RemoveAll(item => TryCreateUserComponent(entity, item.Data, refs));
            if (missing.Items.Count == 0)
            {
                entity.RemoveComponent<MissingComponents>();
            }
        }

        refs.ResolveDeferred();
        return resolved;
    }

    /// <summary>
    /// Turns every user component whose type matches <paramref name="unload"/> back into scene data held by
    /// <see cref="MissingComponents"/>, detaching the live instances — the first half of a script reload. Once
    /// the new types are loaded, <see cref="ResolveMissingComponents"/> rebuilds the components from that data,
    /// fields and entity references included. Lifecycle hooks are not run: reloads happen in edit mode.
    /// </summary>
    /// <param name="scene">The scene to process.</param>
    /// <param name="unload">Selects the component types being unloaded.</param>
    /// <returns>The number of components detached.</returns>
    internal static int UnresolveUserComponents(Scene scene, Func<Type, bool> unload)
    {
        // Every entity needs a stable id first, so an Entity field captured below can be rebound afterwards.
        foreach (Entity entity in scene.View<LabelComponent>())
        {
            entity.EnsurePersistentId();
        }

        int count = 0;
        foreach (Component component in scene.UserComponents)
        {
            if (!unload(component.GetType()))
            {
                continue;
            }

            Entity entity = component.Entity;
            JsonObject data = WriteUserComponent(component);
            entity.RemoveComponent(component.GetType());
            if (!entity.TryGetComponent(out MissingComponents? missing))
            {
                missing = entity.AddComponent(new MissingComponents());
            }

            missing.Items.Add(new MissingComponent(data));
            count++;
        }

        return count;
    }

    private static void AddScript(
        Entity entity, ScriptComponent scripts, string? guid, string className, JsonObject? fields, SceneReferences refs)
    {
        EntityBehaviour? instance = ScriptResolver.Create(guid, className, entity);

        // If the class name was lost (guid-only reference) but the guid resolved, recover the readable name
        // from the resolved instance so the inspector and later saves keep both.
        if (string.IsNullOrEmpty(className) && instance is not null)
        {
            className = instance.GetType().Name;
        }

        if (instance != null && fields != null)
        {
            ComponentSerialization.ApplyMembers(instance, fields, refs);
        }

        scripts.Items.Add(new ScriptInstance(className, instance, guid ?? string.Empty));
    }

    public bool Deserialize(string filepath)
    {
        if (!FileSystem.Current.Exists(filepath))
        {
            return false;
        }

        string json;
        try
        {
            json = FileSystem.Current.ReadAllText(filepath);
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to read scene '{0}': {1}", filepath, ex.Message);
            return false;
        }

        return DeserializeFromString(json);
    }
}
