namespace Spot.Engine.Graphics;

/// <summary>
/// A 2D texture on the GPU, created from raw RGBA pixels. Loading image files is a framework feature
/// (<c>Texture2D.FromFile</c>, <c>Image</c>); this core type only talks to the device.
/// </summary>
public sealed class Texture2D : IDisposable
{
    private readonly IGraphicsDevice _device;
    private TextureHandle _handle;

    /// <summary>
    /// Initializes a new instance of the <see cref="Texture2D"/> class from raw RGBA pixels in memory.
    /// </summary>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="rgbaPixels">The pixel data, four bytes (R, G, B, A) per pixel.</param>
    /// <param name="pointFilter">If true, uses nearest neighbor filtering instead of linear.</param>
    /// <exception cref="ArgumentException">The pixel data is not exactly <c>width * height * 4</c> bytes.</exception>
    public Texture2D(uint width, uint height, ReadOnlySpan<byte> rgbaPixels, bool pointFilter = false)
    {
        // The driver reads width * height * 4 bytes from the span; a short buffer would be a native
        // out-of-bounds read that takes the whole process down, so reject it here.
        if ((ulong)rgbaPixels.Length != (ulong)width * height * 4)
        {
            throw new ArgumentException(
                $"Expected {(ulong)width * height * 4} bytes for a {width}x{height} RGBA texture, got {rgbaPixels.Length}.",
                nameof(rgbaPixels));
        }

        _device = Renderer.Device;
        Width = width;
        Height = height;

        Upload(rgbaPixels, pointFilter);
    }

    /// <summary>
    /// Gets the raw device handle, for issuing commands the wrapper does not expose. Its <c>Id</c> is the native
    /// texture name, which is what UI layers such as ImGui take to display the texture (<c>ImGui.Image</c>).
    /// </summary>
    public TextureHandle Handle => _handle;

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
    public void Dispose()
    {
        if (_handle.Id != 0)
        {
            _device.DeleteTexture(_handle);
            _handle = default;
        }
    }

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
