using Silk.NET.OpenGL;
using StbImageSharp;

namespace Spot.Rendering;

/// <summary>
/// A 2D texture loaded onto the GPU.
/// </summary>
public sealed class Texture2D : IDisposable
{
    private readonly GL _gl;
    private uint _handle;

    /// <summary>
    /// Initializes a new instance of the <see cref="Texture2D"/> class by loading an image from disk.
    /// </summary>
    /// <param name="path">The path to the image file (PNG, JPG, and other formats stb supports).</param>
    public unsafe Texture2D(string path)
    {
        _gl = Renderer.Gl;

        // OpenGL's texture origin is the bottom-left, while image files start at the top-left,
        // so flip vertically on load to keep textures upright.
        StbImage.stbi_set_flip_vertically_on_load(1);
        ImageResult image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);

        Width = (uint)image.Width;
        Height = (uint)image.Height;

        fixed (byte* pixels = image.Data)
        {
            Upload(pixels);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Texture2D"/> class from raw RGBA pixels in memory.
    /// </summary>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="rgbaPixels">The pixel data, four bytes (R, G, B, A) per pixel.</param>
    public unsafe Texture2D(uint width, uint height, ReadOnlySpan<byte> rgbaPixels)
    {
        _gl = Renderer.Gl;
        Width = width;
        Height = height;

        fixed (byte* pixels = rgbaPixels)
        {
            Upload(pixels);
        }
    }

    /// <summary>
    /// Gets the native OpenGL texture handle. Exposed so editor UI can display the texture through
    /// ImGui (for example as an asset thumbnail via <c>ImGui.Image</c>).
    /// </summary>
    public uint Handle => _handle;

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
    public void Bind(uint slot = 0)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + (int)slot);
        _gl.BindTexture(TextureTarget.Texture2D, _handle);
    }

    /// <inheritdoc />
    public void Dispose() => _gl.DeleteTexture(_handle);

    private unsafe void Upload(byte* pixels)
    {
        _handle = _gl.GenTexture();
        Bind();

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);

        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba8,
            Width,
            Height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            pixels);

        _gl.GenerateMipmap(TextureTarget.Texture2D);
    }
}
