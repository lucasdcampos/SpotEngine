using System.Numerics;
using Spot.Rendering;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the editor's 2D grid pass, drawn inside a framework <see cref="Renderer2D"/> batch.
/// </summary>
public class EditorGridTests
{
    private static RecordingGraphicsDevice Install2D()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        Renderer2D.Init();
        return device;
    }

    [Fact]
    public void EditorGrid_FlushesPendingQuadsThenDrawsAFullScreenTriangle()
    {
        RecordingGraphicsDevice device = Install2D();
        Matrix4x4 viewProjection = Matrix4x4.CreateScale(0.5f);

        Renderer2D.BeginScene(viewProjection);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        EditorGrid.Draw2D(zoom: 3f);
        Renderer2D.EndScene();

        Assert.Equal(new[] { (6u, true), (3u, false) }, device.Draws.Select(d => (d.Count, d.Indexed)));
        Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse);
        Assert.Equal(inverse, device.Uniform("uInverseViewProjection"));
        Assert.Equal(3f, device.Uniform("uZoom"));
        Assert.True(device.Capabilities[GraphicsCapability.Blend]);
    }
}
