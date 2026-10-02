using Spot.Framework.IO;
using StbImageSharp;

namespace Spot.Framework.Graphics;

/// <summary>
/// A decoded RGBA8 image in CPU memory: the bridge between image files (PNG, JPG, BMP, TGA, PSD, GIF, HDR) and
/// <see cref="Texture2D"/>. Rows are stored bottom-to-top by default — OpenGL's texture origin — so an image
/// uploads upright.
/// </summary>
public sealed class Image
{
    /// <summary>
    /// Initializes an image from raw pixels.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="rgba">The pixel data, four bytes (R, G, B, A) per pixel, <c>width * height * 4</c> bytes.</param>
    /// <exception cref="ArgumentException">The pixel data does not match the dimensions.</exception>
    public Image(int width, int height, byte[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(rgba);
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException(
                $"Expected {width * height * 4} bytes for a {width}x{height} RGBA image, got {rgba.Length}.",
                nameof(rgba));
        }

        Width = width;
        Height = height;
        Pixels = rgba;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the pixel data, four bytes (R, G, B, A) per pixel.</summary>
    public byte[] Pixels { get; }

    /// <summary>
    /// Decodes an image file read through <see cref="FileSystem"/> (so the path goes through
    /// <see cref="FileSystem.PathResolver"/>).
    /// </summary>
    /// <param name="path">The image file path.</param>
    /// <param name="flipVertically">Store rows bottom-to-top (the GPU texture origin). Defaults to true.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a decodable image.</exception>
    public static Image FromFile(string path, bool flipVertically = true) =>
        FromBytes(FileSystem.ReadAllBytes(path), flipVertically);

    /// <summary>
    /// Decodes an encoded image held in memory.
    /// </summary>
    /// <param name="encoded">The encoded file contents (PNG, JPG, ...).</param>
    /// <param name="flipVertically">Store rows bottom-to-top (the GPU texture origin). Defaults to true.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidDataException">The data is not a decodable image.</exception>
    public static Image FromBytes(byte[] encoded, bool flipVertically = true)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        ImageResult result;
        try
        {
            // Decode upright and flip here instead of through stb's process-wide flip flag, so concurrent decodes
            // (asset cooking, background loads) can never observe each other's setting.
            StbImage.stbi_set_flip_vertically_on_load(0);
            result = ImageResult.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Could not decode image: {ex.Message}", ex);
        }

        var image = new Image(result.Width, result.Height, result.Data);
        if (flipVertically)
        {
            image.FlipVertically();
        }

        return image;
    }

    /// <summary>
    /// Decodes an encoded image from a stream.
    /// </summary>
    /// <param name="stream">The stream holding the encoded image.</param>
    /// <param name="flipVertically">Store rows bottom-to-top (the GPU texture origin). Defaults to true.</param>
    /// <returns>The decoded image.</returns>
    public static Image FromStream(Stream stream, bool flipVertically = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return FromBytes(buffer.ToArray(), flipVertically);
    }

    /// <summary>
    /// Reverses the row order in place.
    /// </summary>
    public void FlipVertically()
    {
        int stride = Width * 4;
        Span<byte> pixels = Pixels;
        byte[] row = new byte[stride];
        for (int top = 0, bottom = Height - 1; top < bottom; top++, bottom--)
        {
            Span<byte> a = pixels.Slice(top * stride, stride);
            Span<byte> b = pixels.Slice(bottom * stride, stride);
            a.CopyTo(row);
            b.CopyTo(a);
            row.CopyTo(b);
        }
    }

    /// <summary>
    /// Uploads the image to a new GPU texture.
    /// </summary>
    /// <param name="pointFilter">Use nearest-neighbor filtering (pixel art) instead of trilinear.</param>
    /// <returns>The new texture.</returns>
    public Texture2D ToTexture(bool pointFilter = false) => new((uint)Width, (uint)Height, Pixels, pointFilter);
}

/// <summary>
/// File loading for <see cref="Texture2D"/>, provided by the framework on top of the core texture type.
/// </summary>
public static class TextureFileLoading
{
    extension(Texture2D)
    {
        /// <summary>
        /// Loads an image file (read through <see cref="FileSystem"/>) into a new texture.
        /// </summary>
        /// <param name="path">The image file path.</param>
        /// <param name="pointFilter">Use nearest-neighbor filtering (pixel art) instead of trilinear.</param>
        /// <returns>The new texture.</returns>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="InvalidDataException">The file is not a decodable image.</exception>
        public static Texture2D FromFile(string path, bool pointFilter = false) =>
            Image.FromFile(path).ToTexture(pointFilter);
    }
}
