using System;
using Silk.NET.OpenGL;

namespace Spot.Rendering;

/// <summary>
/// A rendering target that allows rendering a scene to a texture instead of the screen.
/// </summary>
public sealed class Framebuffer : IDisposable
{
    private readonly GL _gl;
    private uint _rendererId;
    private uint _colorAttachment;
    private uint _depthAttachment;

    public Framebuffer(uint width, uint height)
    {
        _gl = Renderer.Gl;
        Width = width;
        Height = height;
        Invalidate();
    }

    public uint Width { get; private set; }
    public uint Height { get; private set; }

    /// <summary>
    /// Gets the OpenGL texture handle for the color attachment.
    /// </summary>
    public uint ColorAttachment => _colorAttachment;

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _rendererId);
        _gl.Viewport(0, 0, Width, Height);
    }

    public void Unbind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Resize(uint width, uint height)
    {
        if (width == 0 || height == 0 || (Width == width && Height == height))
        {
            return;
        }

        Width = width;
        Height = height;
        Invalidate();
    }

    public void Dispose()
    {
        if (_rendererId != 0)
        {
            _gl.DeleteFramebuffer(_rendererId);
            _gl.DeleteTexture(_colorAttachment);
            _gl.DeleteTexture(_depthAttachment);
        }
    }

    private unsafe void Invalidate()
    {
        if (_rendererId != 0)
        {
            Dispose();
        }

        _rendererId = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _rendererId);

        _colorAttachment = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _colorAttachment);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, Width, Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _colorAttachment, 0);

        _depthAttachment = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _depthAttachment);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Depth24Stencil8, Width, Height, 0, PixelFormat.DepthStencil, PixelType.UnsignedInt248, null);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, TextureTarget.Texture2D, _depthAttachment, 0);

        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException("Framebuffer is incomplete.");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }
}
