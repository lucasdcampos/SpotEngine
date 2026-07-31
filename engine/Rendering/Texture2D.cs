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

        // Resolve project-relative paths against the active project's asset directory so scenes and
        // materials committed with relative texture paths load on any machine.
        path = Spot.Assets.AssetPath.Resolve(path);

        // OpenGL's texture origin is the bottom-left, while image files start at the top-left,
        // so flip vertically on load to keep textures upright.
        StbImage.stbi_set_flip_vertically_on_load(1);
        ImageResult image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);

        Width = (uint)image.Width;
        Height = (uint)image.Height;

        fixed (byte* pixels = image.Data)
        {
            Upload(pixels, false);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Texture2D"/> class from raw RGBA pixels in memory.
    /// </summary>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="rgbaPixels">The pixel data, four bytes (R, G, B, A) per pixel.</param>
    /// <param name="pointFilter">If true, uses nearest neighbor filtering instead of linear.</param>
    public unsafe Texture2D(uint width, uint height, ReadOnlySpan<byte> rgbaPixels, bool pointFilter = false)
    {
        _gl = Renderer.Gl;
        Width = width;
        Height = height;

        fixed (byte* pixels = rgbaPixels)
        {
            Upload(pixels, pointFilter);
        }
    }

    /// <summary>
    /// Gets the native OpenGL texture handle. Exposed so editor UI can display the texture through
    /// ImGui (for example as an asset thumbnail via <c>ImGui.Image</c>).
    /// </summary>
    public uint Handle => _handle;

    /// <summary>
    /// Creates a simple checkerboard texture for debugging.
    /// </summary>
    public static Texture2D CreateCheckerboard()
    {
        uint width = 8;
        uint height = 8;
        byte[] pixels = new byte[width * height * 4];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isLight = (x + y) % 2 == 0;
                byte color = isLight ? (byte)60 : (byte)25;
                
                int index = (y * (int)width + x) * 4;
                pixels[index] = color;
                pixels[index + 1] = color;
                pixels[index + 2] = color;
                pixels[index + 3] = 255;
            }
        }
        
        return new Texture2D(width, height, pixels, pointFilter: true);
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
    public void Bind(uint slot = 0)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + (int)slot);
        _gl.BindTexture(TextureTarget.Texture2D, _handle);
    }

    /// <inheritdoc />
    public void Dispose() => _gl.DeleteTexture(_handle);

    private unsafe void Upload(byte* pixels, bool pointFilter)
    {
        _handle = _gl.GenTexture();
        Bind();

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        
        if (pointFilter)
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        }
        else
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        }

        // Anisotropic filtering keeps textures on surfaces viewed at grazing angles (a ground plane,
        // for example) crisp instead of blurring toward a flat average color. Guarded to a no-op where
        // the extension is unsupported. 0x84FF = GL_MAX_TEXTURE_MAX_ANISOTROPY, 0x84FE = GL_TEXTURE_MAX_ANISOTROPY.
        Span<float> maxAnisotropy = stackalloc float[1];
        maxAnisotropy[0] = 0.0f;
        _gl.GetFloat((GLEnum)0x84FF, maxAnisotropy);
        if (maxAnisotropy[0] > 1.0f)
        {
            _gl.TexParameter(TextureTarget.Texture2D, (GLEnum)0x84FE, Math.Min(8.0f, maxAnisotropy[0]));
        }

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
