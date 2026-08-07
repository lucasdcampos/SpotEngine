using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spot.Assets;
using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// Reads and writes a single entity subtree as a <c>.sptprefab</c> asset — a reusable blueprint that can be
/// instantiated into any scene. It reuses the scene serializer's per-entity machinery
/// (<see cref="SceneSerializer.WriteEntity"/> / <see cref="SceneSerializer.ReadEntity"/>), so a prefab is
/// just one entity node — its components, scripts and children — wrapped in a <c>Root</c> object.
/// </summary>
/// <remarks>
/// Instances created from a prefab are independent, baked copies; editing a prefab definition does not
/// propagate to existing instances. Every path here logs and continues (returning <see langword="null"/> on
/// failure) rather than throwing, per the engine's never-crash rule.
/// </remarks>
public static class Prefab
{
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    /// <summary>Serializes an entity and its descendants to prefab JSON.</summary>
    /// <param name="entity">The entity to capture as a prefab.</param>
    public static string Serialize(Entity entity)
    {
        JsonObject root = SceneSerializer.WriteEntity(entity);

        // A prefab definition must not carry an instance link back to a prefab (including itself).
        root.Remove("Prefab");

        return new JsonObject { ["Root"] = root }.ToJsonString(s_writeOptions);
    }

    /// <summary>
    /// Instantiates a prefab from JSON into a scene under an optional parent, returning the new root entity or
    /// <see langword="null"/> when the JSON is empty or malformed.
    /// </summary>
    /// <param name="scene">The scene to instantiate into.</param>
    /// <param name="json">The prefab JSON.</param>
    /// <param name="parent">The parent entity, or <see langword="null"/> to create a root entity.</param>
    public static Entity? InstantiateInto(Scene scene, string json, Entity? parent)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        // Some editors save JSON with a leading UTF-8 BOM; strip it so the parser doesn't choke.
        json = json.TrimStart('﻿');

        try
        {
            if (JsonNode.Parse(json) is JsonObject rootObj && rootObj["Root"] is JsonObject entityObj)
            {
                return SceneSerializer.ReadEntity(scene, entityObj, parent);
            }

            Log.CoreWarn("Prefab has no 'Root' entity; skipping.");
        }
        catch (JsonException ex)
        {
            Log.CoreError("Failed to parse prefab: {0}", ex.Message);
        }

        return null;
    }

    /// <summary>Instantiates a prefab from a source <c>.sptprefab</c> file (the editor/authoring path).</summary>
    /// <param name="scene">The scene to instantiate into.</param>
    /// <param name="path">The absolute path to the prefab file.</param>
    /// <param name="parent">The parent entity, or <see langword="null"/> to create a root entity.</param>
    public static Entity? InstantiateFile(Scene scene, string path, Entity? parent)
    {
        try
        {
            return InstantiateInto(scene, File.ReadAllText(path), parent);
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to read prefab '{0}': {1}", path, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Instantiates a prefab from a <c>guid:</c> reference, resolving the cooked artifact through the active
    /// content resolver (the manifest at runtime, the Library in the editor).
    /// </summary>
    /// <param name="scene">The scene to instantiate into.</param>
    /// <param name="guidRef">The prefab's <c>guid:</c> reference.</param>
    /// <param name="parent">The parent entity, or <see langword="null"/> to create a root entity.</param>
    public static Entity? InstantiateRef(Scene scene, string guidRef, Entity? parent)
    {
        string? path = AssetPath.ResolveContent(guidRef);
        if (path is null)
        {
            Log.CoreWarn("Could not resolve prefab reference '{0}'.", guidRef);
            return null;
        }

        return InstantiateFile(scene, path, parent);
    }
}
