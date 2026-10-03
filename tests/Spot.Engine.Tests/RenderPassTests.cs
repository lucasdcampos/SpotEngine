using System.Numerics;
using Spot.Engine.Rendering;
using Spot.Engine.Scenes;
using Spot.Framework;
using Spot.Framework.Graphics;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers custom render passes: they run at their stage of the real scene pipeline, in order, with the camera and
/// bound target, and a faulty pass is isolated.
/// </summary>
public class RenderPassTests
{
    private static readonly Matrix4x4 ViewProjection = Matrix4x4.CreateScale(0.5f);
    private static readonly Vector3 Camera = new(1, 2, 3);

    private static RecordingGraphicsDevice Install()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        Renderer3D.Init();
        Renderer.SetViewport(0, 0, 640, 360);
        return device;
    }

    private static void Render(Scene scene) => RenderSystem.Render(scene, ViewProjection, Camera);

    [Fact]
    public void Passes_RunAtTheirStagesInPipelineOrder()
    {
        Install();
        var scene = new Scene();
        var seen = new List<RenderStage>();
        foreach (RenderStage stage in new[] { RenderStage.Overlay, RenderStage.BeforeOpaque, RenderStage.AfterPostProcess,
                     RenderStage.AfterTransparent, RenderStage.AfterOpaque })
        {
            scene.AddRenderPass(new DelegateRenderPass(stage, ctx => seen.Add(ctx.Stage)));
        }

        Render(scene);

        Assert.Equal(
            new[] { RenderStage.BeforeOpaque, RenderStage.AfterOpaque, RenderStage.AfterTransparent, RenderStage.AfterPostProcess, RenderStage.Overlay },
            seen);
    }

    [Fact]
    public void Passes_GetTheCameraAndTheBoundTarget()
    {
        Install();
        var scene = new Scene();
        RenderContext? captured = null;
        scene.AddRenderPass(new DelegateRenderPass(RenderStage.AfterOpaque, ctx => captured = ctx));
        using var target = new Framebuffer(320, 180);
        target.Bind();

        Render(scene);

        RenderContext context = Assert.NotNull(captured);
        Assert.Same(scene, context.Scene);
        Assert.Equal(ViewProjection, context.ViewProjection);
        Assert.Equal(Camera, context.CameraPosition);
        Assert.Equal(target.Handle, context.Target);
        Assert.Equal((320u, 180u), (context.ViewportWidth, context.ViewportHeight));
        Assert.False(context.PostProcessing); // no post-processor installed in tests
    }

    [Fact]
    public void Passes_InTheSameStageRunByOrderThenRegistration()
    {
        Install();
        var scene = new Scene();
        var seen = new List<string>();
        scene.AddRenderPass(new DelegateRenderPass(RenderStage.Overlay, _ => seen.Add("late"), order: 10));
        scene.AddRenderPass(new DelegateRenderPass(RenderStage.Overlay, _ => seen.Add("first")));
        scene.AddRenderPass(new DelegateRenderPass(RenderStage.Overlay, _ => seen.Add("second")));

        Render(scene);

        Assert.Equal(new[] { "first", "second", "late" }, seen);
    }

    [Fact]
    public void AFaultyPass_IsLoggedOnceAndTheOthersStillRun()
    {
        Install();
        var scene = new Scene();
        int good = 0;
        scene.AddRenderPass(new DelegateRenderPass(RenderStage.AfterOpaque, _ => throw new InvalidOperationException("boom"), name: "Broken"));
        scene.AddRenderPass(new DelegateRenderPass(RenderStage.AfterOpaque, _ => good++));
        using RecordingLogSink log = RecordingLogSink.Capture();

        Render(scene);
        Render(scene);

        Assert.Equal(2, good);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Broken"));
    }

    [Fact]
    public void RemovedPasses_StopRunning()
    {
        Install();
        var scene = new Scene();
        int runs = 0;
        var pass = new DelegateRenderPass(RenderStage.Overlay, _ => runs++);
        scene.AddRenderPass(pass);

        Render(scene);
        Assert.True(scene.RemoveRenderPass(pass));
        Assert.False(scene.RemoveRenderPass(pass));
        Render(scene);

        Assert.Equal(1, runs);
        Assert.Equal(0, scene.RenderPasses.Count);
    }

    [Fact]
    public void Passes_CanDrawWithTheFrameworkRenderers()
    {
        RecordingGraphicsDevice device = Install();
        var scene = new Scene();
        scene.AddRenderPass(new DelegateRenderPass(RenderStage.Overlay, ctx =>
        {
            Renderer2D.BeginScene(ctx.ViewProjection);
            Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
            Renderer2D.EndScene();
        }));

        Render(scene);

        Assert.Contains(device.Draws, d => d.Count == 6);
        Assert.Equal(ViewProjection, device.Uniform("uViewProjection"));
    }

    [Fact]
    public void ScenesWithoutPasses_RenderNormally()
    {
        Install();
        var scene = new Scene();

        Render(scene);

        Assert.Empty(scene.RenderPasses.Ordered);
    }

    private sealed class CustomPass : IRenderPass
    {
        public RenderStage Stage => RenderStage.BeforeOpaque;

        public int Runs { get; private set; }

        public void Render(in RenderContext context) => Runs++;
    }

    [Fact]
    public void InterfaceDefaults_ProvideOrderAndName()
    {
        Install();
        var pass = new CustomPass();
        var scene = new Scene();
        scene.AddRenderPass(pass);

        Render(scene);

        IRenderPass asInterface = pass;
        Assert.Equal((0, "CustomPass", 1), (asInterface.Order, asInterface.Name, pass.Runs));
    }
}
