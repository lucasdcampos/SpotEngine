using System.Numerics;
using Spot.Engine.Rendering;
using Spot.Framework.Mathematics;

namespace Spot.Engine.Tests;

public class OcclusionCullerTests
{
    // A camera at (0,0,5) looking at the origin down -Z, matching System.Numerics' row-vector
    // convention (clip = worldPoint * viewProjection), which is what the culler projects with.
    private static Matrix4x4 ViewProjection()
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 1.0f, 0.1f, 100f);
        return view * projection;
    }

    // A wall at the origin, face-on to the camera: 6x6 units across and thin in depth. At a distance of
    // 5 with a 60 degree vertical field of view the visible half-height is tan(30) * 5 = 2.89, so this
    // more than fills the screen.
    private static Aabb3d FullScreenWall() => new(Vector3.Zero, new Vector3(6f, 6f, 0.2f));

    // The same wall shrunk to 2x2 units, which covers only the middle third or so of the screen.
    private static Aabb3d SmallWall() => new(Vector3.Zero, new Vector3(2f, 2f, 0.2f));

    private static OcclusionCuller WithOccluder(in Aabb3d wall)
    {
        var culler = new OcclusionCuller();
        Assert.True(culler.Begin(ViewProjection(), 256));
        culler.AddOccluder(wall, Matrix4x4.Identity);
        return culler;
    }

    [Fact]
    public void IsOccluded_BoxBehindWall_IsOccluded()
    {
        OcclusionCuller culler = WithOccluder(FullScreenWall());
        var hidden = new Aabb3d(new Vector3(0, 0, -5), Vector3.One);

        Assert.Equal(1, culler.OccluderCount);
        Assert.True(culler.IsOccluded(hidden));
    }

    [Fact]
    public void IsOccluded_BoxInFrontOfWall_IsVisible()
    {
        OcclusionCuller culler = WithOccluder(FullScreenWall());
        // Between the camera (z = 5) and the wall (z = 0), so nothing is in front of it.
        var inFront = new Aabb3d(new Vector3(0, 0, 2), Vector3.One);

        Assert.False(culler.IsOccluded(inFront));
    }

    [Fact]
    public void IsOccluded_BoxBesideWallSilhouette_IsVisible()
    {
        OcclusionCuller culler = WithOccluder(SmallWall());
        // Farther than the wall, but off to the side of the strip of screen the wall covers.
        var beside = new Aabb3d(new Vector3(4, 0, -5), Vector3.One);

        Assert.False(culler.IsOccluded(beside));
    }

    [Fact]
    public void IsOccluded_BoxLargerThanWallSilhouette_IsVisible()
    {
        OcclusionCuller culler = WithOccluder(SmallWall());
        // Directly behind the wall but wider than it, so its edges still show around it.
        var overflowing = new Aabb3d(new Vector3(0, 0, -5), new Vector3(8f, 8f, 1f));

        Assert.False(culler.IsOccluded(overflowing));
    }

    [Fact]
    public void IsOccluded_TheOccluderItself_IsNeverCulled()
    {
        Aabb3d wall = FullScreenWall();
        OcclusionCuller culler = WithOccluder(wall);

        // A box's nearest depth is never behind its own rasterized surface, so an occluder can never
        // cull itself — the property that keeps the whole test free of self-inflicted popping.
        Assert.False(culler.IsOccluded(wall));
    }

    [Fact]
    public void IsOccluded_NoOccluders_IsVisible()
    {
        var culler = new OcclusionCuller();
        Assert.True(culler.Begin(ViewProjection(), 256));

        Assert.Equal(0, culler.OccluderCount);
        Assert.False(culler.IsOccluded(new Aabb3d(new Vector3(0, 0, -5), Vector3.One)));
    }

    [Fact]
    public void IsOccluded_BoxSurroundingTheCamera_IsVisible()
    {
        OcclusionCuller culler = WithOccluder(FullScreenWall());
        // Straddles the camera plane: part of it is behind the viewer, so the projection says nothing
        // useful and the box must be kept.
        var atCamera = new Aabb3d(new Vector3(0, 0, 5), new Vector3(4f, 4f, 4f));

        Assert.False(culler.IsOccluded(atCamera));
    }

    [Fact]
    public void AddOccluder_WallTurnedEdgeOn_StopsOccluding()
    {
        var culler = new OcclusionCuller();
        Assert.True(culler.Begin(ViewProjection(), 256));

        // The same wall rotated a quarter turn about Y is now 0.2 units wide on screen instead of 6, so
        // it no longer covers a box behind it. This only holds if the world matrix is really applied.
        culler.AddOccluder(FullScreenWall(), Matrix4x4.CreateRotationY(MathF.PI / 2f));

        Assert.False(culler.IsOccluded(new Aabb3d(new Vector3(0, 0, -5), Vector3.One)));
    }

    [Fact]
    public void AddOccluder_SubPixelOccluder_CoversNothing()
    {
        var culler = new OcclusionCuller();
        Assert.True(culler.Begin(ViewProjection(), 256));

        // Far too small to fill a whole pixel: conservative rasterization writes nothing at all, so it
        // is not counted and cannot cull.
        culler.AddOccluder(new Aabb3d(Vector3.Zero, new Vector3(0.001f, 0.001f, 0.001f)), Matrix4x4.Identity);

        Assert.Equal(0, culler.OccluderCount);
        Assert.False(culler.IsOccluded(new Aabb3d(new Vector3(0, 0, -5), Vector3.One)));
    }

    [Fact]
    public void IsOccluded_OrthographicCamera_StillOccludes()
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(10f, 10f, 0.1f, 100f);

        var culler = new OcclusionCuller();
        Assert.True(culler.Begin(view * projection, 256));
        culler.AddOccluder(FullScreenWall(), Matrix4x4.Identity);

        Assert.Equal(1, culler.OccluderCount);
        Assert.True(culler.IsOccluded(new Aabb3d(new Vector3(0, 0, -5), Vector3.One)));
        Assert.False(culler.IsOccluded(new Aabb3d(new Vector3(0, 0, 2), Vector3.One)));
    }

    [Fact]
    public void IsOccluded_GroundSlabUnderfoot_HidesOnlyWhatIsBuried()
    {
        // A third-person setup: a 1000x1x1000 ground slab with the camera standing just
        // above it, looking along it. The slab runs under and behind the viewer, so this also exercises
        // clipping an occluder against the camera plane.
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 1.9f, 5.4f), new Vector3(0, 1.9f, 0), Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(45f * MathF.PI / 180f, 16f / 9f, 0.1f, 1000f);

        var culler = new OcclusionCuller();
        Assert.True(culler.Begin(view * projection, 256));
        culler.AddOccluder(
            new Aabb3d(Vector3.Zero, Vector3.One),
            Matrix4x4.CreateScale(1000f, 1f, 1000f) * Matrix4x4.CreateTranslation(0f, -0.44f, 0f));

        Assert.Equal(1, culler.OccluderCount);

        // Standing on the slab, below the horizon and so inside its silhouette — but in front of it.
        // Culling this is what would make a player vanish while walking around.
        Assert.False(culler.IsOccluded(new Aabb3d(new Vector3(0, 0.5f, 0), Vector3.One)));

        // Buried: the line of sight to it passes through a solid metre of ground.
        Assert.True(culler.IsOccluded(new Aabb3d(new Vector3(0, -3f, -10f), Vector3.One)));
    }

    [Fact]
    public void Begin_DegenerateMatrix_StaysInactive()
    {
        var culler = new OcclusionCuller();

        Assert.False(culler.Begin(default, 256));

        // Inactive means fail-safe: occluders are ignored and nothing is ever reported as hidden.
        culler.AddOccluder(FullScreenWall(), Matrix4x4.Identity);
        Assert.Equal(0, culler.OccluderCount);
        Assert.False(culler.IsOccluded(new Aabb3d(new Vector3(0, 0, -5), Vector3.One)));
    }

    [Fact]
    public void Begin_ClampsResolutionAndKeepsAspect()
    {
        var culler = new OcclusionCuller();

        Assert.True(culler.Begin(ViewProjection(), 4));
        Assert.Equal(OcclusionCuller.MinResolution, culler.Width);

        Assert.True(culler.Begin(ViewProjection(), 100000));
        Assert.Equal(OcclusionCuller.MaxResolution, culler.Width);

        Assert.True(culler.Begin(ViewProjection(), 256));
        Assert.Equal(256, culler.Width);
        Assert.Equal(144, culler.Height);
    }

    [Fact]
    public void Begin_ClearsThePreviousFrame()
    {
        var culler = new OcclusionCuller();
        Assert.True(culler.Begin(ViewProjection(), 256));
        culler.AddOccluder(FullScreenWall(), Matrix4x4.Identity);
        Assert.True(culler.IsOccluded(new Aabb3d(new Vector3(0, 0, -5), Vector3.One)));

        // A wall that went away last frame must not keep culling this one.
        Assert.True(culler.Begin(ViewProjection(), 256));
        Assert.Equal(0, culler.OccluderCount);
        Assert.False(culler.IsOccluded(new Aabb3d(new Vector3(0, 0, -5), Vector3.One)));
    }

    [Fact]
    public void ScreenAreaFraction_RanksOccludersByHowMuchTheyCover()
    {
        Matrix4x4 viewProjection = ViewProjection();

        float wall = OcclusionCuller.ScreenAreaFraction(viewProjection, FullScreenWall());
        float small = OcclusionCuller.ScreenAreaFraction(viewProjection, SmallWall());
        float distant = OcclusionCuller.ScreenAreaFraction(viewProjection, new Aabb3d(new Vector3(0, 0, -80), Vector3.One));

        Assert.Equal(1f, wall, 3);
        Assert.True(small < wall);
        Assert.True(distant < small);
        Assert.True(distant > 0f);
    }

    [Fact]
    public void ScreenAreaFraction_BoxBehindTheCamera_CoversNothing()
    {
        Matrix4x4 viewProjection = ViewProjection();

        float behind = OcclusionCuller.ScreenAreaFraction(viewProjection, new Aabb3d(new Vector3(0, 0, 40), Vector3.One));

        Assert.Equal(0f, behind);
    }
}
