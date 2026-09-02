namespace Spot.Assets;

/// <summary>
/// Cooks font files (<c>.ttf</c>, <c>.otf</c>) into <c>.sptfont</c>. The runtime rasterizes glyphs to an atlas
/// itself (see <see cref="Rendering.Font"/>), so cooking wraps the original file bytes with a stable header and
/// name rather than baking pixels. This gives fonts the same guid-referenced identity every other cooked asset
/// has, so a <c>Text</c> component can reference a font by guid instead of the engine copying fonts through by
/// path.
/// </summary>
public sealed class FontImporter : IAssetImporter
{
    /// <inheritdoc />
    public string Id => "font";

    /// <inheritdoc />
    public IEnumerable<string> SourceExtensions => new[] { ".ttf", ".otf" };

    /// <inheritdoc />
    public string CookedExtension => ".sptfont";

    /// <inheritdoc />
    public CookedArtifact Cook(string sourcePath, AssetMeta meta, IGuidResolver resolver)
    {
        byte[] ttf = File.ReadAllBytes(sourcePath);
        string name = Path.GetFileNameWithoutExtension(sourcePath);
        byte[] bytes = SpFont.Write(name, ttf);
        return new CookedArtifact(bytes, Id);
    }
}
