using System.Numerics;
using Spot.Engine;
using Spot.Engine.Graphics;

namespace Spot.Engine.Scenes;

/// <summary>
/// Where in the engine's scene pipeline a <see cref="IRenderPass"/> runs.
/// </summary>
public enum RenderStage
{
    /// <summary>After the shadow maps, before any opaque geometry: backgrounds, custom skies, pre-depth.</summary>
    BeforeOpaque,

    /// <summary>After the opaque meshes and 2D sprites (depth is filled), before particles and world text.</summary>
    AfterOpaque,

    /// <summary>
    /// After particles and world text — still inside the HDR capture when post-processing is on, so what you draw
    /// is tone-mapped and blooms like the rest of the scene.
    /// </summary>
    AfterTransparent,

    /// <summary>After post-processing resolved to the output, before the screen-space UI: crisp, untone-mapped.</summary>
    AfterPostProcess,

    /// <summary>The very last thing drawn, over the UI: debug overlays, cursors, fades.</summary>
    Overlay,
}

/// <summary>
/// What a <see cref="IRenderPass"/> gets to draw with: the scene, the camera, and the render target and viewport
/// bound at that point of the pipeline.
/// </summary>
/// <param name="Scene">The scene being rendered.</param>
/// <param name="Stage">The stage the pass is running at.</param>
/// <param name="ViewProjection">The camera's view-projection matrix.</param>
/// <param name="CameraPosition">The camera's world position.</param>
/// <param name="Target">The framebuffer bound for drawing (the HDR capture, an editor viewport, or the screen).</param>
/// <param name="ViewportWidth">The viewport width in pixels.</param>
/// <param name="ViewportHeight">The viewport height in pixels.</param>
/// <param name="PostProcessing">Whether the scene is being captured for post-processing (HDR) this frame.</param>
public readonly record struct RenderContext(
    Scene Scene,
    RenderStage Stage,
    Matrix4x4 ViewProjection,
    Vector3 CameraPosition,
    FramebufferHandle Target,
    uint ViewportWidth,
    uint ViewportHeight,
    bool PostProcessing);

/// <summary>
/// Custom drawing injected into the engine's scene pipeline — the way to render something the engine has no
/// component for, with any of the framework's tools (<see cref="Renderer.Device"/>, <see cref="Renderer2D"/>,
/// <see cref="BasicRenderer3D"/>, <see cref="BillboardBatch"/>, your own shaders). Register it on a scene with
/// <see cref="Scene.AddRenderPass"/>.
/// </summary>
/// <remarks>
/// Restore any state you change that the engine relies on (depth test, blending, the bound target). A pass that
/// throws is logged once and skipped, never taking the frame down.
/// </remarks>
public interface IRenderPass
{
    /// <summary>Gets the stage the pass runs at.</summary>
    RenderStage Stage { get; }

    /// <summary>Gets the order among passes of the same stage; lower runs first. Defaults to 0.</summary>
    int Order => 0;

    /// <summary>Gets a display name for logs and the profiler. Defaults to the class name.</summary>
    string Name => GetType().Name;

    /// <summary>Draws.</summary>
    /// <param name="context">The scene, camera and target at this point of the pipeline.</param>
    void Render(in RenderContext context);
}

/// <summary>
/// An <see cref="IRenderPass"/> from a callback, for passes that need no state of their own.
/// </summary>
public sealed class DelegateRenderPass : IRenderPass
{
    private readonly Action<RenderContext> _render;

    /// <summary>
    /// Wraps a callback as a render pass.
    /// </summary>
    /// <param name="stage">The stage it runs at.</param>
    /// <param name="render">The drawing callback.</param>
    /// <param name="order">The order among passes of the same stage.</param>
    /// <param name="name">A display name.</param>
    public DelegateRenderPass(RenderStage stage, Action<RenderContext> render, int order = 0, string name = "Render Pass")
    {
        ArgumentNullException.ThrowIfNull(render);
        Stage = stage;
        Order = order;
        Name = name;
        _render = render;
    }

    /// <inheritdoc />
    public RenderStage Stage { get; }

    /// <inheritdoc />
    public int Order { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public void Render(in RenderContext context) => _render(context);
}

/// <summary>
/// The custom <see cref="IRenderPass"/>es a <see cref="Scene"/> runs, by stage and order.
/// </summary>
public sealed class RenderPassRegistry
{
    private readonly List<IRenderPass> _passes = new();
    private readonly HashSet<IRenderPass> _faultLogged = new();
    private IRenderPass[]? _ordered;

    /// <summary>Gets the registered passes in execution order (by stage, then order, then registration).</summary>
    public IReadOnlyList<IRenderPass> Ordered =>
        _ordered ??= _passes.OrderBy(static p => p.Stage).ThenBy(static p => p.Order).ToArray();

    /// <summary>Gets the number of registered passes.</summary>
    public int Count => _passes.Count;

    /// <summary>Registers a pass. It runs from the next frame.</summary>
    /// <param name="pass">The pass.</param>
    public void Add(IRenderPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        _passes.Add(pass);
        _ordered = null;
    }

    /// <summary>Removes every registered pass.</summary>
    internal void Clear()
    {
        _passes.Clear();
        _faultLogged.Clear();
        _ordered = null;
    }

    /// <summary>Removes a pass.</summary>
    /// <param name="pass">The pass.</param>
    /// <returns><see langword="true"/> if it was registered.</returns>
    public bool Remove(IRenderPass pass)
    {
        _faultLogged.Remove(pass);
        _ordered = null;
        return _passes.Remove(pass);
    }

    /// <summary>
    /// Runs every pass registered for the context's stage, each in its own guard: a pass that throws is logged once
    /// and skipped.
    /// </summary>
    /// <param name="context">The render context for the stage.</param>
    internal void Run(in RenderContext context)
    {
        if (_passes.Count == 0)
        {
            return;
        }

        foreach (IRenderPass pass in Ordered)
        {
            if (pass.Stage != context.Stage)
            {
                continue;
            }

            Profiler.BeginSample(pass.Name);
            try
            {
                pass.Render(context);
            }
            catch (Exception ex)
            {
                if (_faultLogged.Add(pass))
                {
                    Log.CoreError("Render pass '{0}' threw and will be skipped when it faults. {1}", pass.Name, ex);
                }
            }
            finally
            {
                Profiler.EndSample(pass.Name);
            }
        }
    }
}
