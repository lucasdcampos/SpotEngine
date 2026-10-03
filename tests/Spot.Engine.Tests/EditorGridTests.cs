using System.Numerics;
using Spot.Engine.Rendering;
using Spot.Framework.Graphics;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the editor grid: the 2D pass drawn inside a framework <see cref="Renderer2D"/> batch, and the 3D
/// ground-plane and Y-axis passes drawn over a rendered scene.
/// </summary>
public class EditorGridTests
{
    private static readonly EditorGridStyle Style = EditorGridStyle.Default with
    {
        AxisXColor = new Vector4(1, 0, 0, 1),
        AxisYColor = new Vector4(0, 1, 0, 1),
        AxisZColor = new Vector4(0, 0, 1, 1),
    };

    private static RecordingGraphicsDevice Install()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        Renderer2D.Init();
        return device;
    }

    [Fact]
    public void Draw2D_FlushesPendingQuadsThenDrawsAFullScreenTriangle()
    {
        RecordingGraphicsDevice device = Install();
        Matrix4x4 viewProjection = Matrix4x4.CreateScale(0.5f);

        Renderer2D.BeginScene(viewProjection);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        EditorGrid.Draw2D(Style);
        Renderer2D.EndScene();

        Assert.Equal(new[] { (6u, true), (3u, false) }, device.Draws.Select(d => (d.Count, d.Indexed)));
        Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse);
        Assert.Equal(inverse, device.Uniform("uInverseViewProjection"));
        Assert.True(device.Capabilities[GraphicsCapability.Blend]);
    }

    [Fact]
    public void Draw2D_DrawsTheXAndYAxesAsPartOfTheGrid()
    {
        RecordingGraphicsDevice device = Install();

        Renderer2D.BeginScene(Matrix4x4.Identity);
        EditorGrid.Draw2D(Style);
        Renderer2D.EndScene();

        Assert.Equal(Style.AxisXColor, device.Uniform("uAxisColorU"));
        Assert.Equal(Style.AxisYColor, device.Uniform("uAxisColorV"));
        Assert.Equal(Style.MinorLineColor, device.Uniform("uMinorLineColor"));
        Assert.Equal(Style.MajorLineColor, device.Uniform("uMajorLineColor"));
    }

    [Fact]
    public void Draw3D_DrawsTheGroundPlaneThenTheYAxisWithTheirOwnShaders()
    {
        RecordingGraphicsDevice device = Install();
        Matrix4x4 viewProjection = Matrix4x4.CreateLookAt(new Vector3(3, 4, 5), Vector3.Zero, Vector3.UnitY)
            * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, 16f / 9f, 0.1f, 1000f);
        var camera = new Vector3(3, 4, 5);

        EditorGrid.Draw3D(viewProjection, camera, Style);

        Assert.Equal(2, device.Draws.Count);
        Assert.All(device.Draws, d => Assert.Equal((3u, false), (d.Count, d.Indexed)));
        Assert.NotEqual(device.Draws[0].Program, device.Draws[1].Program);

        Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse);
        Assert.Equal(viewProjection, device.Uniform("uViewProjection"));
        Assert.Equal(inverse, device.Uniform("uInverseViewProjection"));
        Assert.Equal(camera, device.Uniform("uCameraPos"));
        Assert.Equal(Style.AxisXColor, device.Uniform("uAxisColorU"));
        Assert.Equal(Style.AxisZColor, device.Uniform("uAxisColorV"));
        Assert.Equal(Style.AxisYColor, device.Uniform("uAxisColorY"));
    }

    [Fact]
    public void Draw3D_IsDepthTestedButLeavesTheSceneDepthUntouched()
    {
        RecordingGraphicsDevice device = Install();
        Matrix4x4 viewProjection = Matrix4x4.CreateLookAt(new Vector3(0, 2, 5), Vector3.Zero, Vector3.UnitY)
            * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, 1f, 0.1f, 1000f);

        EditorGrid.Draw3D(viewProjection, new Vector3(0, 2, 5));

        Assert.Equal(2, device.Draws.Count);
        Assert.All(device.Draws, d => Assert.False(d.DepthWrite));
        Assert.True(device.Capabilities[GraphicsCapability.DepthTest]);
        Assert.True(device.DepthWrite);
    }

    [Fact]
    public void Draw3D_LookingStraightDown_LeavesOutTheYAxis()
    {
        RecordingGraphicsDevice device = Install();
        var camera = new Vector3(0.3f, 15, 0.3f);
        Matrix4x4 viewProjection = Matrix4x4.CreateLookAt(camera, new Vector3(0.3f, 0, 0.3f), -Vector3.UnitZ)
            * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, 1f, 0.1f, 1000f);

        EditorGrid.Draw3D(viewProjection, camera, Style);

        Assert.Single(device.Draws);
        Assert.Null(device.Uniform("uAxisColorY"));
    }

    [Fact]
    public void Draw3D_WithASingularViewProjection_DrawsNothing()
    {
        RecordingGraphicsDevice device = Install();

        EditorGrid.Draw3D(new Matrix4x4(), Vector3.Zero);

        Assert.Empty(device.Draws);
    }
}
