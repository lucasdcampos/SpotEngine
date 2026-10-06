using System.Text.Json.Nodes;
using Spot.Engine.Graphics;

namespace Spot.Engine.Assets;

/// <summary>
/// Cooks image files (PNG, JPG, ...) into <c>.spttex</c>: raw RGBA pixels decoded once here so the runtime needs
/// no image decoder. Decoding goes through the same <see cref="Image"/> decoder (and vertical flip) as loading the
/// source directly, so a cooked texture uploads verbatim and looks identical to it.
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

        // Same decode as Texture2D.FromFile: OpenGL's origin is bottom-left, so the flip is baked at cook time.
        Image image = Image.FromBytes(File.ReadAllBytes(sourcePath));

        byte[] bytes = SpTex.Write((uint)image.Width, (uint)image.Height, image.Pixels, pointFilter);
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
