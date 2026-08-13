using System.Text.Json.Nodes;
using StbImageSharp;

namespace Spot.Assets;

/// <summary>
/// Cooks image files (PNG, JPG, ...) into <c>.spttex</c>: raw RGBA pixels decoded once here so the runtime needs
/// no image decoder. Decoding applies the same vertical flip the direct <see cref="Rendering.Texture2D"/> loader
/// does, so a cooked texture uploads verbatim and looks identical to loading the source.
/// </summary>
public sealed class TextureImporter : IAssetImporter
{
    /// <inheritdoc />
    public string Id => "texture";

    /// <inheritdoc />
    public IEnumerable<string> SourceExtensions => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };

    /// <inheritdoc />
    public string CookedExtension => ".spttex";

    /// <inheritdoc />
    public CookedArtifact Cook(string sourcePath, AssetMeta meta, IGuidResolver resolver)
    {
        bool pointFilter = ReadPointFilter(meta.Settings);

        // Match Texture2D(string): OpenGL's origin is bottom-left, so bake the vertical flip at cook time.
        StbImage.stbi_set_flip_vertically_on_load(1);
        ImageResult image = ImageResult.FromMemory(File.ReadAllBytes(sourcePath), ColorComponents.RedGreenBlueAlpha);

        byte[] bytes = SpTex.Write((uint)image.Width, (uint)image.Height, image.Data, pointFilter);
        return new CookedArtifact(bytes, Id);
    }

    private static bool ReadPointFilter(JsonObject? settings)
    {
        if (settings is not null
            && settings.TryGetPropertyValue("pointFilter", out JsonNode? node)
            && node is JsonValue value
            && value.TryGetValue(out bool flag))
        {
            return flag;
        }

        return false;
    }
}
