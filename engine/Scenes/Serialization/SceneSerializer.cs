using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    internal static JsonObject WriteEntity(Entity entity)
    {
        var obj = new JsonObject();

        // Tag is structural: it holds the name, the entity's enabled state, and its category tag.
        var tag = entity.GetComponent<LabelComponent>();
        var tagObj = new JsonObject { ["Name"] = tag.Name, ["Enabled"] = entity.Enabled };
        if (!string.IsNullOrEmpty(tag.Tag))
        {
            tagObj["Tag"] = tag.Tag;
        }

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

        // A scene with no "Entities" array is valid (an empty scene) rather than an error.
        if (rootObj["Entities"] is JsonArray entities)
        {
            foreach (JsonNode? entityNode in entities)
            {
                if (entityNode is JsonObject entityObj)
                {
                    ReadEntity(_scene, entityObj, null);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Reads a single entity (and its descendants) from a JSON object into the given scene under an optional
    /// parent, returning the created entity. Shared with the prefab serializer, which instantiates one such
    /// subtree. Component and script failures are logged and skipped rather than thrown.
    /// </summary>
    internal static Entity ReadEntity(Scene scene, JsonObject entityObj, Entity? parent)
    {
        string name = "Entity";
        bool enabled = true;
        string tag = string.Empty;
        if (entityObj["Tag"] is JsonObject tagObj)
        {
            name = tagObj["Name"]?.GetValue<string>() ?? "Entity";
            enabled = tagObj["Enabled"]?.GetValue<bool>() ?? true;
            tag = tagObj["Tag"]?.GetValue<string>() ?? string.Empty;
        }

        var entity = scene.Instantiate(name);
        entity.Enabled = enabled;
        entity.Tag = tag;
        if (parent != null)
        {
            entity.SetParent(parent);
        }

        foreach (var (key, node) in entityObj)
        {
            if (key is "Tag" or "Children" || node is not JsonObject componentObj)
            {
                continue;
            }

            if (key == "Scripts")
            {
                DeserializeScripts(entity, componentObj);
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
                    ReadEntity(scene, childObj, entity);
                }
            }
        }

        return entity;
    }

    private static void DeserializeScripts(Entity entity, JsonObject data)
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
                if (!string.IsNullOrEmpty(className))
                {
                    AddScript(entity, scripts, className, entry["Fields"] as JsonObject);
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
                    AddScript(entity, scripts, className, null);
                }
            }
        }
    }

    private static void AddScript(Entity entity, ScriptComponent scripts, string className, JsonObject? fields)
    {
        EntityBehaviour? instance = ScriptResolver.Create(className, entity);
        if (instance != null && fields != null)
        {
            ComponentSerialization.ApplyMembers(instance, fields);
        }

        scripts.Items.Add(new ScriptInstance(className, instance));
    }

    public bool Deserialize(string filepath)
    {
        if (!File.Exists(filepath))
        {
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(filepath);
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to read scene '{0}': {1}", filepath, ex.Message);
            return false;
        }

        return DeserializeFromString(json);
    }
}
