using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Spot.Scenes;

namespace Spot.Assets;

/// <summary>
/// Cooks a <c>.sptprefab</c> prefab by rewriting any asset references it contains — on its entities'
/// components, at any depth — from source paths to stable <c>guid:</c> references, so the prefab and the
/// assets it uses survive rename/move and the runtime resolves them through the manifest. The JSON is
/// otherwise passed through unchanged: a prefab is already engine-native scene data (a single entity subtree).
/// </summary>
public sealed class PrefabImporter : IAssetImporter
{
    // Component reference-property keys whose values are asset paths (kept in sync with the scene migrator).
    private static readonly string[] ReferenceKeys = { "ModelPath", "MaterialPath", "TexturePath", "NormalMapPath", "ClipPath" };

    /// <inheritdoc />
    public string Id => "prefab";

    /// <inheritdoc />
    public IEnumerable<string> SourceExtensions => new[] { ".sptprefab" };

    /// <inheritdoc />
    public string CookedExtension => ".sptprefab";

    /// <inheritdoc />
    public CookedArtifact Cook(string sourcePath, AssetMeta meta, IGuidResolver resolver)
    {
        JsonNode? root = SceneJson.Parse(File.ReadAllText(sourcePath))
            ?? throw new InvalidDataException($"Prefab '{sourcePath}' is not valid JSON.");

        Rewrite(root, resolver);

        byte[] bytes = Encoding.UTF8.GetBytes(root.ToJsonString(SceneJson.WriteOptions));
        return new CookedArtifact(bytes, Id);
    }

    // Recursively rewrites reference-property string values to guid: references anywhere in the tree, so it
    // works regardless of component schema (nested Children, arbitrary components).
    private static void Rewrite(JsonNode? node, IGuidResolver resolver)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (string key in obj.Select(kv => kv.Key).ToList())
                {
                    if (Array.IndexOf(ReferenceKeys, key) >= 0
                        && obj[key] is JsonValue value
                        && value.TryGetValue(out string? path)
                        && !string.IsNullOrEmpty(path))
                    {
                        obj[key] = resolver.ToGuidRef(path) ?? path;
                    }
                    else
                    {
                        Rewrite(obj[key], resolver);
                    }
                }

                break;

            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    Rewrite(item, resolver);
                }

                break;
        }
    }
}
