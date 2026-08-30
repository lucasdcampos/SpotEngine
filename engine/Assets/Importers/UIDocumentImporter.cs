using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Spot.Assets;

/// <summary>
/// Cooks a <c>.sptui</c> UI document by rewriting its texture and font references — on any widget, at any
/// depth — from source paths to stable <c>guid:</c> references, so the document and the assets it uses survive
/// rename/move and the runtime resolves them through the manifest. The JSON is otherwise passed through
/// unchanged: a UI document is already engine-native (a widget tree).
/// </summary>
public sealed class UIDocumentImporter : IAssetImporter
{
    // Widget reference-property keys whose values are asset paths (kept in sync with UISerializer's *Ref fields).
    private static readonly string[] ReferenceKeys = { "TextureRef", "SpriteRef", "FontRef" };

    /// <inheritdoc />
    public string Id => "uidoc";

    /// <inheritdoc />
    public IEnumerable<string> SourceExtensions => new[] { ".sptui" };

    /// <inheritdoc />
    public string CookedExtension => ".sptui";

    /// <inheritdoc />
    public CookedArtifact Cook(string sourcePath, AssetMeta meta, IGuidResolver resolver)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(sourcePath))
            ?? throw new InvalidDataException($"UI document '{sourcePath}' is not valid JSON.");

        Rewrite(root, resolver);

        byte[] bytes = Encoding.UTF8.GetBytes(root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return new CookedArtifact(bytes, Id);
    }

    // Recursively rewrites reference-property string values to guid: references anywhere in the tree, so it
    // works regardless of widget schema (nested Children, arbitrary widget types).
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
