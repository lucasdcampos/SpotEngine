using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Spot.Assets;

/// <summary>
/// Cooks a <c>.sptmat</c> material by rewriting its texture references from source paths to stable
/// <c>guid:</c> references, so the material and its textures survive rename/move and the runtime resolves them
/// through the manifest. The material JSON is otherwise passed through unchanged — it is already engine-native.
/// </summary>
public sealed class MaterialImporter : IAssetImporter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <inheritdoc />
    public string Id => "material";

    /// <inheritdoc />
    public IEnumerable<string> SourceExtensions => new[] { ".sptmat" };

    /// <inheritdoc />
    public string CookedExtension => ".sptmat";

    /// <inheritdoc />
    public CookedArtifact Cook(string sourcePath, AssetMeta meta, IGuidResolver resolver)
    {
        JsonObject obj = JsonNode.Parse(File.ReadAllText(sourcePath))?.AsObject()
            ?? throw new InvalidDataException($"Material '{sourcePath}' is not a JSON object.");

        RewriteRef(obj, "TexturePath", resolver);
        RewriteRef(obj, "NormalMapPath", resolver);

        byte[] bytes = Encoding.UTF8.GetBytes(obj.ToJsonString(Options));
        return new CookedArtifact(bytes, Id);
    }

    private static void RewriteRef(JsonObject obj, string key, IGuidResolver resolver)
    {
        if (obj.TryGetPropertyValue(key, out JsonNode? node)
            && node is JsonValue value
            && value.TryGetValue(out string? path)
            && !string.IsNullOrEmpty(path))
        {
            obj[key] = resolver.ToGuidRef(path) ?? path;
        }
    }
}
