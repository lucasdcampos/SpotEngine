using System;
using Spot.Core;

namespace Spot.Rendering;

/// <summary>
/// A framebuffer backed by a depth cubemap, used for real-time point/spot light shadow maps.
/// The caller renders the scene six times (one face per pass) into this object, then binds
/// the resulting cubemap in the lit shader for shadow lookups.
/// </summary>
internal sealed class PointShadowMap : IDisposable
{
    private FramebufferHandle _fbo;
    private TextureHandle _cubemap;

    public uint Size { get; }

    public TextureHandle Cubemap => _cubemap;

    public PointShadowMap(uint size)
    {
        Size = size;
        Build();
    }

    private void Build()
    {
        IGraphicsDevice device = Renderer.Device;

        _cubemap = device.CreateCubemapTexture();
        device.BindCubemapTexture(0, _cubemap);
        for (uint i = 0; i < 6; i++)
        {
            device.CubemapFaceImage(i, TextureInternalFormat.DepthComponent32F, Size);
        }
        device.SetCubemapFilter(TextureFilter.Nearest, TextureFilter.Nearest);
        device.SetCubemapWrap(TextureWrap.ClampToEdge);

        _fbo = device.CreateFramebuffer();
        device.BindFramebuffer(_fbo);
        // Attach face 0 as a placeholder so the FBO is not incomplete at creation time.
        device.FramebufferCubeFace(RenderTargetAttachment.Depth, _cubemap, 0);
        device.SetColorBuffersNone();

        if (!device.CheckFramebufferComplete())
        {
            Log.CoreError("PointShadowMap framebuffer is incomplete.");
        }

        // Restore the default framebuffer so subsequent work doesn't inadvertently draw here.
        Renderer.BindRenderTarget(FramebufferHandle.Default,
            Renderer.ViewportX, Renderer.ViewportY, Renderer.ViewportWidth, Renderer.ViewportHeight);
    }

    /// <summary>
    /// Attaches <paramref name="face"/> (0–5) of the cubemap to the FBO's depth slot, binds the FBO,
    /// sets the viewport to the cubemap size, and clears the depth buffer. The caller must already have
    /// saved the previous render target (see Renderer3D.BeginPointShadowPass).
    /// </summary>
    public void BeginFace(uint face)
    {
        // BindRenderTarget both binds the FBO and updates Renderer's tracking (CurrentRenderTarget,
        // viewport), so EndPointShadowPass can correctly restore to the saved target.
        Renderer.BindRenderTarget(_fbo, 0, 0, Size, Size);
        Renderer.Device.FramebufferCubeFace(RenderTargetAttachment.Depth, _cubemap, face);
        Renderer.ClearDepth();
    }

    /// <summary>Binds the cubemap depth texture to the given texture unit for sampling in lit shaders.</summary>
    public void BindTexture(uint unit) => Renderer.Device.BindCubemapTexture(unit, _cubemap);

    public void Dispose()
    {
        if (_fbo.Id != 0)
        {
            Renderer.Device.DeleteFramebuffer(_fbo);
            Renderer.Device.DeleteCubemapTexture(_cubemap);
            _fbo = default;
            _cubemap = default;
        }
    }
}
