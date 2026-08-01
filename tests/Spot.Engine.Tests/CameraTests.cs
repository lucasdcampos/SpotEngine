using System.Numerics;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Engine.Tests;

public class CameraTests
{
    [Fact]
    public void Orthographic_ProducesAffineProjection()
    {
        var cam = new CameraComponent { ProjectionType = SceneCameraProjection.Orthographic };

        // Orthographic projections stay affine (M44 == 1); perspective sets M44 to 0.
        Assert.Equal(1.0, cam.Projection.M44, 3);
    }

    [Fact]
    public void Perspective_ProducesProjectiveMatrix()
    {
        var cam = new CameraComponent { ProjectionType = SceneCameraProjection.Perspective };

        Assert.Equal(0.0, cam.Projection.M44, 3);
        Assert.Equal(-1.0, cam.Projection.M34, 3);
    }

    [Fact]
    public void SetViewportSize_ChangesAspectRatio()
    {
        var cam = new CameraComponent { ProjectionType = SceneCameraProjection.Orthographic, ZoomLevel = 5f };
        float square = cam.Projection.M11;

        cam.SetViewportSize(200, 100); // 2:1 aspect
        float wide = cam.Projection.M11;

        Assert.NotEqual(square, wide);
    }

    [Fact]
    public void SetViewportSize_IsIgnoredWhenFixedAspectRatio()
    {
        var cam = new CameraComponent { FixedAspectRatio = true };
        float before = cam.Projection.M11;

        cam.SetViewportSize(1920, 1080);

        Assert.Equal(before, cam.Projection.M11, 5);
    }

    [Fact]
    public void GetViewProjection_AtOrigin_EqualsProjection()
    {
        var cam = new CameraComponent { ProjectionType = SceneCameraProjection.Orthographic };
        var scene = new Scene();
        var transform = scene.Instantiate().GetComponent<TransformComponent>(); // identity at origin

        Matrix4x4 vp = cam.GetViewProjection(transform);

        // The view of an origin camera with no rotation is the identity, so VP collapses to Projection.
        AssertMatrixClose(cam.Projection, vp);
    }

    private static void AssertMatrixClose(Matrix4x4 a, Matrix4x4 b)
    {
        const float tol = 1e-4f;
        bool close =
            MathF.Abs(a.M11 - b.M11) < tol && MathF.Abs(a.M22 - b.M22) < tol &&
            MathF.Abs(a.M33 - b.M33) < tol && MathF.Abs(a.M44 - b.M44) < tol &&
            MathF.Abs(a.M41 - b.M41) < tol && MathF.Abs(a.M42 - b.M42) < tol &&
            MathF.Abs(a.M43 - b.M43) < tol;
        Assert.True(close, $"Matrices differ:\nexpected {a}\nactual   {b}");
    }
}
