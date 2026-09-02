using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spot.Assets;
using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// Reads and writes a <see cref="Scene"/> as <c>.sptscene</c> JSON. Component data is handled entirely
/// by reflection through <see cref="ComponentSerialization"/> — every component tagged with
/// <see cref="SceneComponentAttribute"/> is written and read automatically, so adding a serializable
/// component needs no changes here. Only the two structural pieces are handled explicitly: the
/// <see cref="LabelComponent"/> (it carries the entity name and drives <see cref="Scene.Instantiate"/>)
/// and the <see cref="ScriptComponent"/> (its scripts are resolved by class name and instantiated at
/// load). The reader tolerates missing files, empty input, a UTF-8 BOM, malformed JSON, unknown
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
    internal static Entity ReadEntity(Scene scene, JsonObject entityObj, Entity? parent)
    {
        var refs = new SceneReferences();
        Entity entity = ReadEntitySubtree(scene, entityObj, parent, refs, freshIds: true);
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
        if (!AssetProvider.Current.Exists(filepath))
        {
            return false;
        }

        string json;
        try
        {
            json = AssetProvider.Current.ReadAllText(filepath);
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to read scene '{0}': {1}", filepath, ex.Message);
            return false;
        }

        return DeserializeFromString(json);
    }
}
