using System.Numerics;
using Spot.Tests.Fakes;
using Spot.Rendering;

namespace Spot.Framework.Tests;

/// <summary>
/// Covers the screen-space UI batcher against a recording device: lazy setup, pass state, the top-left
/// projection, scissor clipping and text.
/// </summary>
public class UIRendererTests
{
    private static RecordingGraphicsDevice Install()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        return device;
    }

    [Fact]
    public void Pass_InitializesLazilyAndBatchesQuads()
    {
        UIRenderer.Shutdown();
        RecordingGraphicsDevice device = Install();

        UIRenderer.Begin(800, 600);
        UIRenderer.DrawQuad(new Vector2(10, 10), new Vector2(50, 20), Vector4.One);
        UIRenderer.DrawQuad(new Vector2(70, 10), new Vector2(50, 20), Vector4.One);
        UIRenderer.End();

        Assert.Equal(12u, Assert.Single(device.Draws).Count);
        Assert.Equal((800f, 600f), (UIRenderer.ScreenWidth, UIRenderer.ScreenHeight));
    }

    [Fact]
    public void Pass_SetsBlendedNoDepthStateAndRestoresIt()
    {
        RecordingGraphicsDevice device = Install();

        UIRenderer.Begin(100, 100);
        Assert.False(device.Capabilities[GraphicsCapability.DepthTest]);
        Assert.True(device.Capabilities[GraphicsCapability.Blend]);
        Assert.Equal((BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha), device.BlendFunc);

        UIRenderer.End();
        Assert.True(device.Capabilities[GraphicsCapability.DepthTest]);
        Assert.False(device.Capabilities[GraphicsCapability.Blend]);
        Assert.False(device.Capabilities[GraphicsCapability.ScissorTest]);
    }

    [Fact]
    public void Pass_UsesATopLeftOriginProjection()
    {
        RecordingGraphicsDevice device = Install();

        UIRenderer.Begin(640, 480);
        UIRenderer.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        UIRenderer.End();

        var projection = Assert.IsType<Matrix4x4>(device.Uniform("uProjection"));
        Vector4 topLeft = Vector4.Transform(new Vector4(0, 0, 0, 1), projection);
        Vector4 bottomRight = Vector4.Transform(new Vector4(640, 480, 0, 1), projection);
        Assert.Equal((-1f, 1f), (topLeft.X, topLeft.Y));
        Assert.Equal((1f, -1f), (bottomRight.X, bottomRight.Y));
    }

    [Fact]
    public void Clip_FlushesAndSetsABottomLeftScissorThatIntersectsNestedClips()
    {
        RecordingGraphicsDevice device = Install();

        UIRenderer.Begin(200, 100);
        UIRenderer.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        UIRenderer.PushClip(new Vector4(10, 20, 100, 50));
        Assert.Single(device.Draws); // geometry under the old clip was drawn first
        Assert.Equal((10, 30, 100u, 50u), device.Scissor);

        UIRenderer.PushClip(new Vector4(50, 0, 200, 40));
        Assert.Equal((50, 60, 60u, 20u), device.Scissor); // intersection: x 50..110, y 20..40

        UIRenderer.PopClip();
        Assert.Equal((10, 30, 100u, 50u), device.Scissor);

        UIRenderer.PopClip();
        Assert.False(device.Capabilities[GraphicsCapability.ScissorTest]);
        UIRenderer.End();
    }

    [Fact]
    public void DrawText_EmitsOneQuadPerVisibleGlyphAndMeasuresConsistently()
    {
        RecordingGraphicsDevice device = Install();
        Font font = Font.Default;

        UIRenderer.Begin(400, 300);
        Vector2 drawn = UIRenderer.DrawText(font, "Hi!", new Vector2(5, 5), 24f, Vector4.One, TextLayoutOptions.Default);
        UIRenderer.End();

        Vector2 measured = UIRenderer.MeasureText(font, "Hi!", 24f, TextLayoutOptions.Default);
        Assert.Equal(measured, drawn);
        Assert.Equal(3u * 6u, device.Draws.Sum(d => d.Count));
    }
}
