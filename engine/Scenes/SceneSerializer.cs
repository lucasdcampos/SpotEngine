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
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

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
                entities.Add(SerializeEntity(entity));
            }
        }

        var root = new JsonObject { ["Entities"] = entities };
        return root.ToJsonString(s_writeOptions);
    }

    private JsonObject SerializeEntity(Entity entity)
    {
        var obj = new JsonObject();

        // Tag is structural: it holds the name and the entity's enabled state.
        var tag = entity.GetComponent<LabelComponent>();
        obj["Tag"] = new JsonObject { ["Name"] = tag.Name, ["Enabled"] = entity.Enabled };

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
                childArray.Add(SerializeEntity(child));
            }
            obj["Children"] = childArray;
        }

        return obj;
    }

    private static JsonObject SerializeScripts(ScriptComponent scripts)
    {
        var names = new JsonArray();
        foreach (string name in scripts.ClassNames)
        {
            names.Add(name);
        }

        return new JsonObject { ["Enabled"] = scripts.Enabled, ["ScriptNames"] = names };
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
            root = JsonNode.Parse(json);
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
                    DeserializeEntity(entityObj, null);
                }
            }
        }

        return true;
    }

    private void DeserializeEntity(JsonObject entityObj, Entity? parent)
    {
        string name = "Entity";
        bool enabled = true;
        if (entityObj["Tag"] is JsonObject tagObj)
        {
            name = tagObj["Name"]?.GetValue<string>() ?? "Entity";
            enabled = tagObj["Enabled"]?.GetValue<bool>() ?? true;
        }

        var entity = _scene.Instantiate(name);
        entity.Enabled = enabled;
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
                    DeserializeEntity(childObj, entity);
                }
            }
        }
    }

    private static void DeserializeScripts(Entity entity, JsonObject data)
    {
        var scripts = new ScriptComponent { Enabled = data["Enabled"]?.GetValue<bool>() ?? true };
        entity.AddComponent(scripts);

        if (data["ScriptNames"] is not JsonArray names)
        {
            return;
        }

        foreach (JsonNode? nameNode in names)
        {
            string? scriptName = nameNode?.GetValue<string>();
            if (string.IsNullOrEmpty(scriptName))
            {
                continue;
            }

            scripts.ClassNames.Add(scriptName);

            Type? scriptType = ResolveScriptType(scriptName);
            if (scriptType == null)
            {
                Log.CoreWarn("Failed to load script '{0}'. Type not found.", scriptName);
                continue;
            }

            try
            {
                var instance = (EntityBehaviour)Activator.CreateInstance(scriptType)!;
                instance.Entity = entity;
                scripts.Scripts.Add(instance);
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to instantiate script '{0}': {1}", scriptName, ex.Message);
            }
        }
    }

    // Resolves a script class name (an optional ".cs" suffix is tolerated) to an EntityBehaviour type,
    // searching every loaded assembly so game scripts in the project assembly are found by reflection.
    private static Type? ResolveScriptType(string scriptName)
    {
        string className = scriptName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            ? scriptName[..^3]
            : scriptName;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                // A partially-loadable assembly must never take scene loading down; skip it.
                continue;
            }

            Type? match = types.FirstOrDefault(t => t.Name == className && t.IsSubclassOf(typeof(EntityBehaviour)));
            if (match != null)
            {
                return match;
            }
        }

        return null;
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
