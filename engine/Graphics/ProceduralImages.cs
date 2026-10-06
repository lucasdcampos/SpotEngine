using System.Numerics;

namespace Spot.Engine.Graphics;

/// <summary>
/// Generates common utility images in code — solid colors, a flat normal map, a checkerboard, a prototyping grid
/// and a soft dot — as CPU <see cref="Image"/>s. Upload one with <see cref="Image.ToTexture"/>, or save it with
/// <see cref="Image.SavePng"/>.
/// </summary>
/// <remarks>
/// Rows run bottom-up, like every <see cref="Image"/> bound for a texture. Colors are RGBA in <c>[0, 1]</c>.
/// </remarks>
public static class ProceduralImages
{
    /// <summary>Creates an image of one color.</summary>
    /// <param name="color">The color.</param>
    /// <param name="size">The width and height in pixels.</param>
    /// <returns>The image.</returns>
    public static Image Solid(Vector4 color, int size = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        (byte r, byte g, byte b, byte a) = ToBytes(color);
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = a;
        }

        return new Image(size, size, pixels);
    }

    /// <summary>Creates a flat tangent-space normal map: every texel points straight out of the surface.</summary>
    /// <param name="size">The width and height in pixels.</param>
    /// <returns>The image.</returns>
    public static Image FlatNormal(int size = 4) => Solid(new Vector4(0.5f, 0.5f, 1.0f, 1.0f), size);

    /// <summary>
    /// Creates a checkerboard. The defaults are a soft two-tone gray that averages to a smooth tone at a distance
    /// instead of shimmering when heavily tiled.
    /// </summary>
    /// <param name="size">The width and height in pixels.</param>
    /// <param name="cells">The squares along each side.</param>
    /// <param name="light">The color of the squares at the corners; defaults to a dark gray.</param>
    /// <param name="dark">The other squares' color; defaults to a darker gray.</param>
    /// <returns>The image.</returns>
    public static Image Checkerboard(int size = 256, int cells = 8, Vector4? light = null, Vector4? dark = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cells);
        (byte, byte, byte, byte) a = ToBytes(light ?? new Vector4(72 / 255f, 72 / 255f, 72 / 255f, 1));
        (byte, byte, byte, byte) b = ToBytes(dark ?? new Vector4(48 / 255f, 48 / 255f, 48 / 255f, 1));
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool first = ((x * cells / size) + (y * cells / size)) % 2 == 0;
                Put(pixels, (y * size + x) * 4, first ? a : b);
            }
        }

        return new Image(size, size, pixels);
    }

    /// <summary>
    /// Creates a prototyping grid: a flat background crossed by thin lines between cells and a heavier line on the
    /// tile's edges, so a tiled surface shows both its scale and where each tile repeats.
    /// </summary>
    /// <param name="size">The width and height in pixels.</param>
    /// <param name="cells">The cells along each side.</param>
    /// <param name="background">The background color; defaults to a mid gray.</param>
    /// <param name="line">The line color; defaults to a lighter gray.</param>
    /// <returns>The image.</returns>
    public static Image Grid(int size = 512, int cells = 8, Vector4? background = null, Vector4? line = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cells);
        (byte, byte, byte, byte) fill = ToBytes(background ?? new Vector4(0.36f, 0.37f, 0.40f, 1));
        (byte, byte, byte, byte) ink = ToBytes(line ?? new Vector4(0.62f, 0.64f, 0.68f, 1));
        int minor = Math.Max(1, size / 256);
        int major = Math.Max(2, size / 96);
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool onLine = Near(x, size, cells, minor) || Near(y, size, cells, minor)
                    || Edge(x, size, major) || Edge(y, size, major);
                Put(pixels, (y * size + x) * 4, onLine ? ink : fill);
            }
        }

        return new Image(size, size, pixels);
    }

    /// <summary>
    /// Creates a soft round dot: white, opaque at the center and fading smoothly to transparent at the edge — the
    /// default look of particles and billboards.
    /// </summary>
    /// <param name="size">The width and height in pixels.</param>
    /// <returns>The image.</returns>
    public static Image SoftDot(int size = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        var pixels = new byte[size * size * 4];
        float center = (size - 1) * 0.5f;
        float radius = MathF.Max(center, 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / radius;
                float dy = (y - center) / radius;
                float t = Math.Clamp(1.0f - MathF.Sqrt(dx * dx + dy * dy), 0.0f, 1.0f);
                float alpha = t * t * (3.0f - 2.0f * t); // smoothstep(1, 0, distance)
                Put(pixels, (y * size + x) * 4, (255, 255, 255, (byte)(alpha * 255.0f)));
            }
        }

        return new Image(size, size, pixels);
    }

    // Whether a coordinate lies on an interior cell boundary, within a line half-width on either side.
    private static bool Near(int coordinate, int size, int cells, int halfWidth)
    {
        for (int i = 1; i < cells; i++)
        {
            int boundary = i * size / cells;
            if (coordinate >= boundary - halfWidth && coordinate < boundary + halfWidth)
            {
                return true;
            }
        }

        return false;
    }

    // Whether a coordinate lies on the tile's outer edge (half the line on each side, so tiles join to a full line).
    private static bool Edge(int coordinate, int size, int width) =>
        coordinate < (width + 1) / 2 || coordinate >= size - width / 2;

    private static void Put(byte[] pixels, int index, (byte R, byte G, byte B, byte A) color)
    {
        pixels[index] = color.R;
        pixels[index + 1] = color.G;
        pixels[index + 2] = color.B;
        pixels[index + 3] = color.A;
    }

    private static (byte R, byte G, byte B, byte A) ToBytes(Vector4 color)
    {
        static byte Channel(float v) => (byte)MathF.Round(Math.Clamp(v, 0.0f, 1.0f) * 255.0f);
        return (Channel(color.X), Channel(color.Y), Channel(color.Z), Channel(color.W));
    }
}
