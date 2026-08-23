using System;

namespace Spot.Rendering;

/// <summary>
/// A framebuffer that only attaches a depth texture, optimized for shadow map generation. Backend-neutral:
/// it issues every command through <see cref="Renderer.Device"/>, so it drives desktop OpenGL and browser
/// WebGL2 alike.
/// </summary>
public sealed class DepthFramebuffer : IDisposable
{
    private FramebufferHandle _framebuffer;
    private TextureHandle _depthAttachment;

    public DepthFramebuffer(uint width, uint height)
    {
        Width = width;
        Height = height;
        Invalidate();
    }

    public uint Width { get; private set; }
    public uint Height { get; private set; }

    /// <summary>Gets the depth texture handle sampled as the shadow map.</summary>
    public TextureHandle DepthAttachment => _depthAttachment;

    public void Bind() => Renderer.BindRenderTarget(_framebuffer, 0, 0, Width, Height);

    public void Unbind() => Renderer.Device.BindFramebuffer(FramebufferHandle.Default);

    public void BindDepthTexture(uint slot = 1) => Renderer.Device.BindTexture(slot, _depthAttachment);

    public void Dispose()
    {
        if (_framebuffer.Id != 0)
        {
            Renderer.Device.DeleteFramebuffer(_framebuffer);
            Renderer.Device.DeleteTexture(_depthAttachment);
            _framebuffer = default;
            _depthAttachment = default;
        }
    }

    private void Invalidate()
    {
        if (_framebuffer.Id != 0)
        {
            Dispose();
        }

        IGraphicsDevice device = Renderer.Device;

        _depthAttachment = device.CreateTexture();
        device.BindTexture(0, _depthAttachment);
        device.TextureImage2D(TextureInternalFormat.DepthComponent32F, Width, Height, ReadOnlySpan<byte>.Empty);

        // Linear filtering + hardware depth comparison turns every tap through a sampler2DShadow into a
        // bilinear percentage-closer sample, which smooths the blocky shadow edges (LEQUAL compare).
        device.SetTextureFilter(TextureFilter.Linear, TextureFilter.Linear);
        device.SetTextureCompareMode(true);

        // Clamp to edge (WebGL2 has no clamp-to-border): the shaders test the projected coords against [0,1]
        // and treat anything outside the shadow region as fully lit, so edge-smearing is avoided without a
        // border color.
        device.SetTextureWrap(TextureWrap.ClampToEdge);

        _framebuffer = device.CreateFramebuffer();
        device.BindFramebuffer(_framebuffer);
        device.FramebufferTexture2D(RenderTargetAttachment.Depth, _depthAttachment);
        device.SetColorBuffersNone();

        if (!device.CheckFramebufferComplete())
        {
            throw new InvalidOperationException("Depth Framebuffer is incomplete.");
        }

        Renderer.BindRenderTarget(FramebufferHandle.Default, Renderer.ViewportX, Renderer.ViewportY, Renderer.ViewportWidth, Renderer.ViewportHeight);
    }
}
