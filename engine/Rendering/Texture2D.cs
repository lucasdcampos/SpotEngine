using Spot.Assets;
using StbImageSharp;

namespace Spot.Rendering;

/// <summary>
/// A 2D texture loaded onto the GPU.
/// </summary>
public sealed class Texture2D : IDisposable
{
    private readonly IGraphicsDevice _device;
    private TextureHandle _handle;

    /// <summary>
    /// Initializes a new instance of the <see cref="Texture2D"/> class by loading an image from disk.
    /// </summary>
    /// <param name="path">The path to the image file (PNG, JPG, and other formats stb supports).</param>
    public Texture2D(string path)
    {
        _device = Renderer.Device;

        // Resolve project-relative paths against the active project's asset directory so scenes and
        // materials committed with relative texture paths load on any machine.
        path = Spot.Assets.AssetPath.Resolve(path);

        // OpenGL's texture origin is the bottom-left, while image files start at the top-left,
        // so flip vertically on load to keep textures upright.
        StbImage.stbi_set_flip_vertically_on_load(1);
        ImageResult image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);

        Width = (uint)image.Width;
        Height = (uint)image.Height;

        Upload(image.Data, false);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Texture2D"/> class from raw RGBA pixels in memory.
    /// </summary>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="rgbaPixels">The pixel data, four bytes (R, G, B, A) per pixel.</param>
    /// <param name="pointFilter">If true, uses nearest neighbor filtering instead of linear.</param>
    public Texture2D(uint width, uint height, ReadOnlySpan<byte> rgbaPixels, bool pointFilter = false)
    {
        _device = Renderer.Device;
        Width = width;
        Height = height;

        Upload(rgbaPixels, pointFilter);
    }

    /// <summary>
    /// Gets the native texture handle. Exposed so editor UI can display the texture through
    /// ImGui (for example as an asset thumbnail via <c>ImGui.Image</c>).
    /// </summary>
    public uint Handle => _handle.Id;

    /// <summary>
    /// Creates a soft checkerboard texture for debugging. Rendered at a real resolution (not one texel
    /// per square) with a gentle two-tone contrast so trilinear filtering can antialias the edges up
    /// close and, crucially, average the pattern into smooth gray at distance instead of shimmering
    /// like TV static across large, heavily-tiled surfaces.
    /// </summary>
    public static Texture2D CreateCheckerboard()
    {
        const int size = 256;      // texture resolution
        const int squares = 8;     // checker squares per axis (one texture tile)
        const int squarePx = size / squares;
        byte[] pixels = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool isLight = ((x / squarePx) + (y / squarePx)) % 2 == 0;
                byte color = isLight ? (byte)72 : (byte)48;

                int index = (y * size + x) * 4;
                pixels[index] = color;
                pixels[index + 1] = color;
                pixels[index + 2] = color;
                pixels[index + 3] = 255;
            }
        }

        // pointFilter: false -> linear + mipmaps + anisotropy, the key to killing the distance shimmer.
        return new Texture2D((uint)size, (uint)size, pixels, pointFilter: false);
    }

    /// <summary>
    /// Loads a cooked <c>.spttex</c> texture — raw RGBA decoded at import time — and uploads it verbatim, so no
    /// image decoder runs at runtime. Mipmaps are generated on upload, as for a source texture.
    /// </summary>
    /// <param name="path">The absolute path to the cooked <c>.spttex</c> file.</param>
    public static Texture2D FromSpTex(string path)
    {
        SpTexData tex = SpTex.ReadFile(path);
        return new Texture2D(tex.Width, tex.Height, tex.Rgba, tex.PointFilter);
    }

    /// <summary>
    /// Loads a texture from a stored reference: a <c>guid:</c> reference resolves to its cooked <c>.spttex</c>
    /// through the content host, while any other value is loaded as a source image path. This is the single
    /// entry point components and materials use, so they never care whether the project has been cooked.
    /// </summary>
    /// <param name="storedRef">The stored reference (a <c>guid:</c> reference or a source image path).</param>
    /// <exception cref="FileNotFoundException">A <c>guid:</c> reference has no cooked artifact.</exception>
    public static Texture2D Load(string storedRef)
    {
        if (AssetRef.IsGuidRef(storedRef))
        {
            string cooked = AssetPath.ResolveContent(storedRef)
                ?? throw new FileNotFoundException($"Unresolved texture reference '{storedRef}'.");
            return FromSpTex(cooked);
        }

        return new Texture2D(storedRef);
    }

    /// <summary>
    /// Gets the texture width in pixels.
    /// </summary>
    public uint Width { get; }

    /// <summary>
    /// Gets the texture height in pixels.
    /// </summary>
    public uint Height { get; }

    /// <summary>
    /// Binds the texture to the given texture unit.
    /// </summary>
    /// <param name="slot">The texture unit index (matching the sampler uniform value).</param>
    public void Bind(uint slot = 0) => _device.BindTexture(slot, _handle);

    /// <inheritdoc />
    public void Dispose() => _device.DeleteTexture(_handle);

    private void Upload(ReadOnlySpan<byte> pixels, bool pointFilter)
    {
        _handle = _device.CreateTexture();
        _device.BindTexture(0, _handle);

        _device.SetTextureWrap(TextureWrap.Repeat);

        if (pointFilter)
        {
            _device.SetTextureFilter(TextureFilter.Nearest, TextureFilter.Nearest);
        }
        else
        {
            _device.SetTextureFilter(TextureFilter.LinearMipmapLinear, TextureFilter.Linear);
        }

        // Anisotropic filtering keeps textures on surfaces viewed at grazing angles (a ground plane,
        // for example) crisp instead of blurring toward a flat average color. A no-op where the
        // extension is unsupported (max anisotropy reported as 1).
        float maxAnisotropy = _device.GetMaxAnisotropy();
        if (maxAnisotropy > 1.0f)
        {
            _device.SetTextureMaxAnisotropy(Math.Min(8.0f, maxAnisotropy));
        }

        _device.TextureImage2DRgba8(Width, Height, pixels);
        _device.GenerateMipmap2D();
    }
}
