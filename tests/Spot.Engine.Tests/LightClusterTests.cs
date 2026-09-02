using System.Numerics;
using Spot.Rendering;

namespace Spot.Engine.Tests;

public class LightClusterTests
{
    // Camera at (0,0,5) looking down -Z at the origin, matching the row-vector convention the froxel builder
    // (and the rest of the engine) uses. Near 0.1, far 100.
    private static (Matrix4x4 vp, Matrix4x4 invVp, Vector3 cam) PerspectiveCamera()
    {
        Vector3 cam = new(0, 0, 5);
        Matrix4x4 view = Matrix4x4.CreateLookAt(cam, Vector3.Zero, Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 1.0f, 0.1f, 100f);
        Matrix4x4 vp = view * proj;
        Matrix4x4.Invert(vp, out Matrix4x4 invVp);
        return (vp, invVp, cam);
    }

    private static (Matrix4x4 vp, Matrix4x4 invVp, Vector3 cam) OrthographicCamera()
    {
        Vector3 cam = new(0, 0, 5);
        Matrix4x4 view = Matrix4x4.CreateLookAt(cam, Vector3.Zero, Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreateOrthographic(10f, 10f, 0.1f, 100f);
        Matrix4x4 vp = view * proj;
        Matrix4x4.Invert(vp, out Matrix4x4 invVp);
        return (vp, invVp, cam);
    }

    private static bool ContainsLight(LightClusters clusters, int lightIndex)
    {
        ReadOnlySpan<uint> grid = clusters.Grid;
        ReadOnlySpan<uint> indices = clusters.Indices;
        for (int f = 0; f < LightClusters.ClusterCount; f++)
        {
            uint packed = grid[f];
            uint offset = packed >> 8;
            uint count = packed & 255u;
            for (uint k = 0; k < count; k++)
            {
                if (indices[(int)(offset + k)] == (uint)lightIndex)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Fact]
    public void Assign_PerspectiveCamera_RecoversNearAndFar()
    {
        (Matrix4x4 vp, Matrix4x4 invVp, Vector3 cam) = PerspectiveCamera();
        var clusters = new LightClusters();

        var lights = new[] { new Renderer3D.PointLightData { Position = Vector3.Zero, Color = Vector3.One, Intensity = 1f, Range = 3f } };
        bool ok = clusters.Assign(vp, invVp, cam, lights);

        Assert.True(ok);
        Assert.Equal(0.1f, clusters.Near, 2);
        Assert.Equal(100f, clusters.Far, 0);
    }

    [Fact]
    public void Assign_LightInView_LandsInSomeFroxel()
    {
        (Matrix4x4 vp, Matrix4x4 invVp, Vector3 cam) = PerspectiveCamera();
        var clusters = new LightClusters();

        // A light at the origin, in front of the camera and within the frustum.
        var lights = new[] { new Renderer3D.PointLightData { Position = Vector3.Zero, Color = Vector3.One, Intensity = 1f, Range = 2f } };
        bool ok = clusters.Assign(vp, invVp, cam, lights);

        Assert.True(ok);
        Assert.True(ContainsLight(clusters, 0), "the in-view light should be assigned to at least one froxel");
    }

    [Fact]
    public void Assign_OrthographicCamera_FallsBack()
    {
        (Matrix4x4 vp, Matrix4x4 invVp, Vector3 cam) = OrthographicCamera();
        var clusters = new LightClusters();

        var lights = new[] { new Renderer3D.PointLightData { Position = Vector3.Zero, Color = Vector3.One, Intensity = 1f, Range = 3f } };
        bool ok = clusters.Assign(vp, invVp, cam, lights);

        Assert.False(ok);
    }

    [Fact]
    public void Assign_ZeroRangeLight_IsIgnored()
    {
        (Matrix4x4 vp, Matrix4x4 invVp, Vector3 cam) = PerspectiveCamera();
        var clusters = new LightClusters();

        var lights = new[] { new Renderer3D.PointLightData { Position = Vector3.Zero, Color = Vector3.One, Intensity = 1f, Range = 0f } };
        bool ok = clusters.Assign(vp, invVp, cam, lights);

        Assert.True(ok);
        Assert.False(ContainsLight(clusters, 0));
    }
}
