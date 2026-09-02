using System.Numerics;
using Spot.Physics;
using Spot.Rendering;

namespace Spot.Engine.Tests;

public class FrustumTests
{
    // A camera at (0,0,5) looking at the origin down -Z, matching System.Numerics' row-vector
    // convention (clip = worldPoint * viewProjection), which is what Frustum extracts planes from.
    private static Matrix4x4 ViewProjection()
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 1.0f, 0.1f, 100f);
        return view * proj;
    }

    [Fact]
    public void Intersects_BoxInFrontOfCamera_IsVisible()
    {
        var frustum = new Frustum(ViewProjection());
        var box = new Aabb3d(Vector3.Zero, Vector3.One);

        Assert.True(frustum.Intersects(box));
    }

    [Fact]
    public void Intersects_BoxBehindCamera_IsCulled()
    {
        var frustum = new Frustum(ViewProjection());
        // Camera sits at z=5 looking toward -Z, so anything at large +Z is behind the near plane.
        var box = new Aabb3d(new Vector3(0, 0, 50), Vector3.One);

        Assert.False(frustum.Intersects(box));
    }

    [Fact]
    public void Intersects_BoxFarToTheSide_IsCulled()
    {
        var frustum = new Frustum(ViewProjection());
        var box = new Aabb3d(new Vector3(1000, 0, 0), Vector3.One);

        Assert.False(frustum.Intersects(box));
    }

    [Fact]
    public void Intersects_DegenerateMatrix_KeepsEverything()
    {
        // An all-zero matrix yields all-zero planes; the test must never cull (fail-safe).
        var frustum = new Frustum(default);
        var box = new Aabb3d(new Vector3(1000, 1000, 1000), Vector3.One);

        Assert.True(frustum.Intersects(box));
    }

    [Fact]
    public void Transform_Translation_MovesCenterKeepsExtents()
    {
        var box = new Aabb3d(Vector3.Zero, Vector3.One); // half-extents (0.5,0.5,0.5)
        Aabb3d moved = box.Transform(Matrix4x4.CreateTranslation(10, -3, 2));

        Assert.Equal(new Vector3(10, -3, 2), moved.Center);
        Assert.Equal(new Vector3(0.5f, 0.5f, 0.5f), moved.HalfExtents);
    }

    [Fact]
    public void Transform_Scale_GrowsExtents()
    {
        var box = new Aabb3d(Vector3.Zero, Vector3.One);
        Aabb3d scaled = box.Transform(Matrix4x4.CreateScale(2f));

        Assert.Equal(new Vector3(1f, 1f, 1f), scaled.HalfExtents);
    }

    [Fact]
    public void Transform_Rotation_GrowsAxisAlignedExtents()
    {
        // A unit box rotated 45° about Z should need a larger axis-aligned box to contain it.
        var box = new Aabb3d(Vector3.Zero, Vector3.One);
        Aabb3d rotated = box.Transform(Matrix4x4.CreateRotationZ(MathF.PI / 4f));

        Assert.True(rotated.HalfExtents.X > 0.5f);
        Assert.True(rotated.HalfExtents.Y > 0.5f);
        // Z is the rotation axis, so it stays put.
        Assert.Equal(0.5f, rotated.HalfExtents.Z, 3);
    }

    [Fact]
    public void Expanded_ScalesHalfExtentsAboutCenter()
    {
        var box = new Aabb3d(new Vector3(1, 2, 3), Vector3.One);
        Aabb3d padded = box.Expanded(2f);

        Assert.Equal(new Vector3(1, 2, 3), padded.Center);
        Assert.Equal(new Vector3(1f, 1f, 1f), padded.HalfExtents);
    }
}
