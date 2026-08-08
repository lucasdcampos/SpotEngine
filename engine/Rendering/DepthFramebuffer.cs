using System;
using Silk.NET.OpenGL;

namespace Spot.Rendering;

/// <summary>
/// A framebuffer that only attaches a depth texture, optimized for shadow map generation.
/// </summary>
public sealed class DepthFramebuffer : IDisposable
{
    private readonly GL _gl;
    private uint _rendererId;
    private uint _depthAttachment;

    public DepthFramebuffer(uint width, uint height)
    {
        _gl = Renderer.Gl;
        Width = width;
        Height = height;
        Invalidate();
    }

    public uint Width { get; private set; }
    public uint Height { get; private set; }
    public uint DepthAttachment => _depthAttachment;

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _rendererId);
        _gl.Viewport(0, 0, Width, Height);
    }

    public void Unbind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }
    
    public void BindDepthTexture(uint slot = 1)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + (int)slot);
        _gl.BindTexture(TextureTarget.Texture2D, _depthAttachment);
    }

    public void Dispose()
    {
        if (_rendererId != 0)
        {
            _gl.DeleteFramebuffer(_rendererId);
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

        _depthAttachment = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _depthAttachment);
        
        // DepthComponent and Float for better precision
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent, Width, Height, 0, PixelFormat.DepthComponent, PixelType.Float, null);
        
        // Linear filtering + hardware depth comparison turns every tap through a sampler2DShadow into a
        // 2x2 bilinear percentage-closer sample, which is what smooths the blocky shadow edges. The
        // shaders sample this as a sampler2DShadow with a LEQUAL compare.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);

        // Clamp-to-border with a "far" (1.0) border so anything sampled outside the shadowed region reads
        // as fully lit instead of smearing the edge texels — the region just stops casting, no artifact.
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        float[] borderColor = { 1.0f, 1.0f, 1.0f, 1.0f };
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBorderColor, borderColor);

        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, _depthAttachment, 0);
        
        _gl.DrawBuffer(DrawBufferMode.None);
        _gl.ReadBuffer(ReadBufferMode.None);

        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException("Depth Framebuffer is incomplete.");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }
}
