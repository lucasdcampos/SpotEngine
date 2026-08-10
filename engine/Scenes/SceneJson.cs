using System.Text.Json;
using System.Text.Json.Nodes;

namespace Spot.Scenes;

/// <summary>
/// Shared JSON settings for reading and writing engine-native entity-tree data (scenes and prefabs).
/// Deeply nested hierarchies exceed <see cref="System.Text.Json"/>'s default nesting cap of 64, which would
/// otherwise throw while serializing or parsing. The worst offenders are rigged models imported from FBX,
/// whose bone tree can run dozens of levels deep, and every hierarchy level contributes two JSON levels (the
/// entity object plus its <c>Children</c> array). Every scene/prefab read and write goes through these
/// settings so the same generous depth limit applies uniformly — otherwise a scene could be written but fail
/// to load, or dirty-tracking could spam errors.
/// </summary>
internal static class SceneJson
{
    /// <summary>
    /// Maximum object/array nesting allowed for scene and prefab JSON. At two JSON levels per hierarchy level
    /// this permits hierarchies hundreds of entities deep — far beyond any real rig — while still bounding
    /// pathological or cyclic structures.
    /// </summary>
    public const int MaxDepth = 512;

    /// <summary>Indented writer options with the raised depth limit; used for all scene/prefab writes.</summary>
    public static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, MaxDepth = MaxDepth };

    private static readonly JsonDocumentOptions s_parseOptions = new() { MaxDepth = MaxDepth };

    /// <summary>
    /// Parses scene/prefab JSON with the raised depth limit. A drop-in for <see cref="JsonNode.Parse(string,
    /// JsonNodeOptions?, JsonDocumentOptions)"/> that keeps callers from repeating the depth setting.
    /// </summary>
    public static JsonNode? Parse(string json) => JsonNode.Parse(json, nodeOptions: null, s_parseOptions);
}
