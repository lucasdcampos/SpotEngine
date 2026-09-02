using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// The desktop <see cref="IScenePostProcessor"/>: captures the scene into an offscreen HDR (<c>RGBA16F</c>)
/// framebuffer, then resolves it to the screen with threshold-gated bloom, ACES tone mapping and FXAA. This is
/// the desktop-only half of the post pipeline — it wraps <see cref="Framebuffer"/>, <see cref="BloomRenderer"/>
/// and <see cref="PostProcessingRenderer"/> (all Silk-backed) so the shared <see cref="RenderSystem"/> can stay
/// backend-neutral. The browser installs no processor and renders straight to the screen (no post) for now.
/// </summary>
public sealed class DesktopScenePostProcessor : IScenePostProcessor
{
    // Number of horizontal+vertical blur pairs used for bloom. Higher widens the glow at a small fill cost;
    // five reads as a soft, wide bloom at half resolution without visible box stepping.
    private const int BloomIterations = 5;

    // The HDR target selected for the current Begin/Resolve pair (drawn from the size-keyed pool below).
    private Framebuffer? _hdrFramebuffer;

    // HDR capture targets, one per distinct render size, in a tiny most-recently-used list. The editor
    // renders several times per frame at different sizes (scene viewport, the game-view panel, and a
    // selected camera's preview). A single shared target was disposed and reallocated on every size switch —
    // several large RGBA16F allocations per frame, which by itself dropped the editor to ~100 FPS whenever a
    // camera was in the scene. Reusing a target per size removes that churn; the cap bounds VRAM and evicts
    // sizes that stop being drawn (e.g. intermediate sizes seen while dragging a panel splitter).
    private readonly List<Framebuffer> _hdrTargets = new();
    private const int MaxHdrTargets = 4;

    // The render target and viewport that were bound before capture, restored on resolve. The previous target
    // is queried from GL state (robust to the editor binding its own framebuffer outside the renderer).
    private int _prevFbo;
    private int _prevX;
    private int _prevY;
    private uint _prevW;
    private uint _prevH;

    /// <inheritdoc />
    public unsafe bool Begin(PostProcessingComponent settings)
    {
        _ = settings;

        int[] currentFbo = new int[1];
        int[] viewport = new int[4];
        float[] clearColor = new float[4];
        fixed (int* ptr = currentFbo) Renderer.Api.GetInteger(Silk.NET.OpenGL.GLEnum.FramebufferBinding, ptr);
        fixed (int* ptr = viewport) Renderer.Api.GetInteger(Silk.NET.OpenGL.GLEnum.Viewport, ptr);
        fixed (float* ptr = clearColor) Renderer.Api.GetFloat(Silk.NET.OpenGL.GLEnum.ColorClearValue, ptr);

        // A minimized (or zero-sized) window reports a 0x0 viewport. Allocating an HDR framebuffer from that
        // produces an invalid, incomplete framebuffer and a stream of GL errors, so decline capture — the
        // scene renders straight to the screen this frame, which is invisible anyway.
        if (viewport[2] <= 0 || viewport[3] <= 0)
        {
            return false;
        }

        _prevFbo = currentFbo[0];
        _prevX = viewport[0];
        _prevY = viewport[1];
        _prevW = (uint)viewport[2];
        _prevH = (uint)viewport[3];

        _hdrFramebuffer = AcquireHdrTarget(_prevW, _prevH);

        // Bind through the renderer so its tracked render-target/viewport state stays correct — the shadow
        // pass saves and restores that state, and would otherwise restore the screen mid-capture.
        Renderer.BindRenderTarget(_hdrFramebuffer.Handle, 0, 0, _prevW, _prevH);
        Renderer.SetClearColor(clearColor[0], clearColor[1], clearColor[2], clearColor[3]);
        Renderer.Clear();
        return true;
    }

    /// <inheritdoc />
    public void Resolve(PostProcessingComponent settings)
    {
        if (_hdrFramebuffer == null)
        {
            return;
        }

        // Extract and blur the scene's bright regions while the HDR buffer is still bound as the source. Bloom
        // manages its own (half-res) targets and leaves nothing bound, so do it before rebinding the target.
        uint bloomTexture = 0;
        if (settings.EnableBloom)
        {
            bloomTexture = BloomRenderer.Generate(
                _hdrFramebuffer.ColorAttachment, (int)_prevW, (int)_prevH, settings.BloomThreshold, BloomIterations);
        }

        Renderer.BindRenderTarget(new FramebufferHandle((uint)_prevFbo), _prevX, _prevY, _prevW, _prevH);

        // Carry the scene's depth from the HDR pass into the target buffer so anything drawn on top afterwards
        // (e.g. the editor grid and world axes) is occluded by the geometry instead of showing through it.
        // Skipped for the default framebuffer (game runtime), where nothing is drawn over the composite.
        if (_prevFbo != 0)
        {
            _hdrFramebuffer.BlitDepthTo((uint)_prevFbo, _prevX, _prevY, _prevW, _prevH);
        }

        PostProcessingRenderer.Draw(_hdrFramebuffer.ColorAttachment, settings, bloomTexture);
    }

    // Returns an HDR capture target of the requested size, reusing a cached one when possible and creating
    // (and, past the cap, evicting the least-recently-used) only on a genuinely new size. Reused targets are
    // moved to the front so the sizes drawn every frame stay resident and transient ones fall off.
    private Framebuffer AcquireHdrTarget(uint width, uint height)
    {
        for (int i = 0; i < _hdrTargets.Count; i++)
        {
            Framebuffer fb = _hdrTargets[i];
            if (fb.Width == width && fb.Height == height)
            {
                if (i != 0)
                {
                    _hdrTargets.RemoveAt(i);
                    _hdrTargets.Insert(0, fb);
                }

                return fb;
            }
        }

        var created = new Framebuffer(width, height, FramebufferFormat.RGBA16F);
        _hdrTargets.Insert(0, created);
        while (_hdrTargets.Count > MaxHdrTargets)
        {
            _hdrTargets[^1].Dispose();
            _hdrTargets.RemoveAt(_hdrTargets.Count - 1);
        }

        return created;
    }
}
