using System;
using System.Numerics;
using Spot.Engine.Mathematics;

namespace Spot.Engine.Graphics;

/// <summary>
/// A software occlusion-culling buffer: a small CPU depth image of the scene's <b>occluders</b> (solid
/// geometry declared as blocking, such as walls, floors and buildings) that candidate objects are then
/// tested against, so geometry hidden behind an occluder is dropped before it ever reaches a draw call.
/// Frustum culling removes what is outside the view; this removes what is inside the view but not
/// actually seen.
/// </summary>
/// <remarks>
/// <para>
/// Usage is one <see cref="Begin"/> per frame, then an <see cref="AddOccluder"/> per blocking object,
/// then an <see cref="IsOccluded"/> per candidate. It is pure CPU work — no graphics device, no GPU
/// queries, no one-frame latency — so it behaves identically on every backend (desktop OpenGL and
/// WebGL2 alike) and is directly unit-testable.
/// </para>
/// <para>
/// The depth stored is normalized-device z, which is linear in screen space (that is why hardware depth
/// buffers interpolate it linearly) and monotone in distance for both perspective and orthographic
/// projections, so the same code serves either camera. Occluders are rasterized <b>conservatively</b>:
/// a pixel is written only when it is <i>fully</i> covered by an occluder triangle, and the depth
/// written is that triangle's <i>farthest</i> depth within the pixel. Candidates are tested the other
/// way around — their screen rectangle is expanded outward to whole pixels and their <i>nearest</i>
/// depth is used, and they are culled only when every pixel of that rectangle holds a nearer occluder.
/// Both sides therefore err toward keeping geometry, and an object is never culled while a sliver of it
/// is still visible.
/// </para>
/// <para>
/// The one non-conservative part is the occluder proxy itself: <see cref="AddOccluder"/> rasterizes the
/// six faces of a box, not the mesh's real triangles, which is what keeps the cost flat (12 triangles
/// per occluder regardless of model complexity). Declaring geometry an occluder is therefore an
/// assertion that its box is solid — right for a wall, a floor slab or a rock, wrong for a hollow
/// building shell or a doorway frame, whose box would swallow what is meant to be seen through it.
/// </para>
/// </remarks>
public sealed class OcclusionCuller
{
    /// <summary>The narrowest buffer width <see cref="Begin"/> accepts; lower values are clamped up.</summary>
    public const int MinResolution = 32;

    /// <summary>The widest buffer width <see cref="Begin"/> accepts; higher values are clamped down.</summary>
    public const int MaxResolution = 1024;

    // Clip-space w at or below this is on/behind the camera plane, where the perspective divide is
    // meaningless. Occluder triangles are clipped against it; candidates that reach it are kept.
    private const float NearW = 1e-4f;

    // Unwritten pixels hold this, so a candidate's "nearer than every pixel" test can never pass over a
    // pixel no occluder covered. Any value past the far plane would do.
    private const float Unwritten = float.MaxValue;

    // A box face is a quad, and clipping it against two planes can add a vertex per plane.
    private const int MaxPolygonVertices = 8;

    private float[] _depth = Array.Empty<float>();
    private Matrix4x4 _viewProjection;
    private int _width;
    private int _height;
    private bool _active;

    /// <summary>Gets the buffer width in pixels, or 0 before the first successful <see cref="Begin"/>.</summary>
    public int Width => _width;

    /// <summary>Gets the buffer height in pixels, or 0 before the first successful <see cref="Begin"/>.</summary>
    public int Height => _height;

    /// <summary>
    /// Gets the number of occluders that actually covered at least one pixel this frame. Occluders too
    /// small or too edge-on to fill a whole pixel contribute nothing and are not counted.
    /// </summary>
    public int OccluderCount { get; private set; }

    /// <summary>
    /// Starts a frame: clears the depth buffer and adopts the camera to project with. Call once before
    /// any <see cref="AddOccluder"/> or <see cref="IsOccluded"/>.
    /// </summary>
    /// <param name="viewProjection">The camera's world-to-clip matrix (the same one the scene renders with).</param>
    /// <param name="resolution">
    /// The buffer width in pixels, clamped to <see cref="MinResolution"/>..<see cref="MaxResolution"/>; the
    /// height follows at 9/16 of it. Low is the point — a coarse buffer costs little to fill and still
    /// resolves a wall, and its coarseness only ever means culling slightly less.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the buffer is ready; <see langword="false"/> for a degenerate
    /// (non-invertible) camera matrix, in which case nothing may be culled and the culler stays inactive.
    /// </returns>
    public bool Begin(Matrix4x4 viewProjection, int resolution)
    {
        _active = false;
        OccluderCount = 0;

        if (!Matrix4x4.Invert(viewProjection, out _))
        {
            return false;
        }

        int width = Math.Clamp(resolution, MinResolution, MaxResolution);
        int height = Math.Max(1, width * 9 / 16);
        if (_width != width || _height != height)
        {
            _depth = new float[width * height];
            _width = width;
            _height = height;
        }

        Array.Fill(_depth, Unwritten);
        _viewProjection = viewProjection;
        _active = true;
        return true;
    }

    /// <summary>
    /// Rasterizes one occluder into the buffer as the six faces of its (transformed, so possibly rotated)
    /// box. Everything strictly behind that box becomes cullable.
    /// </summary>
    /// <param name="localBounds">The occluder's bounds in its own local space.</param>
    /// <param name="world">The occluder's local-to-world matrix.</param>
    public void AddOccluder(in Aabb3d localBounds, in Matrix4x4 world)
    {
        if (!_active)
        {
            return;
        }

        // The eight box corners straight to clip space, indexed by bit: x = 1, y = 2, z = 4.
        Matrix4x4 toClip = world * _viewProjection;
        Vector3 min = localBounds.Min;
        Vector3 max = localBounds.Max;
        Span<Vector4> corners = stackalloc Vector4[8];
        for (int i = 0; i < 8; i++)
        {
            var local = new Vector4(
                (i & 1) == 0 ? min.X : max.X,
                (i & 2) == 0 ? min.Y : max.Y,
                (i & 4) == 0 ? min.Z : max.Z,
                1f);
            corners[i] = Vector4.Transform(local, toClip);
        }

        // The six faces as cyclic quads of corner indices (-x, +x, -y, +y, -z, +z). Winding is
        // irrelevant: each triangle is oriented on the fly, and overlapping faces resolve by nearest
        // depth, so the front face of the box is what ends up occluding.
        ReadOnlySpan<byte> faces = stackalloc byte[]
        {
            0, 2, 6, 4,
            1, 3, 7, 5,
            0, 1, 5, 4,
            2, 3, 7, 6,
            0, 1, 3, 2,
            4, 5, 7, 6,
        };

        bool covered = false;
        Span<Vector4> face = stackalloc Vector4[4];
        for (int f = 0; f < faces.Length; f += 4)
        {
            face[0] = corners[faces[f]];
            face[1] = corners[faces[f + 1]];
            face[2] = corners[faces[f + 2]];
            face[3] = corners[faces[f + 3]];
            covered |= RasterizeClippedFace(face);
        }

        if (covered)
        {
            OccluderCount++;
        }
    }

    /// <summary>
    /// Tests whether a world-space box is entirely hidden behind the occluders added this frame.
    /// </summary>
    /// <param name="worldBounds">The candidate's world-space bounds (the same box frustum culling uses).</param>
    /// <returns>
    /// <see langword="true"/> only when every pixel the box can touch already holds a nearer occluder —
    /// that is, when drawing it could not change the image. <see langword="false"/> whenever there is any
    /// doubt: no occluders, a box crossing the near plane, or a box off the edge of the buffer.
    /// </returns>
    public bool IsOccluded(in Aabb3d worldBounds)
    {
        if (!_active || OccluderCount == 0)
        {
            return false;
        }

        Vector3 min = worldBounds.Min;
        Vector3 max = worldBounds.Max;
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector4(
                (i & 1) == 0 ? min.X : max.X,
                (i & 2) == 0 ? min.Y : max.Y,
                (i & 4) == 0 ? min.Z : max.Z,
                1f);
            Vector4 clip = Vector4.Transform(corner, _viewProjection);

            // Straddling the camera plane: the box reaches the viewer, so there is nothing that could be
            // in front of it — and the perspective divide would be meaningless anyway. Never cull.
            if (!(clip.W > NearW))
            {
                return false;
            }

            Vector3 pixel = ToPixel(clip);
            minX = MathF.Min(minX, pixel.X);
            maxX = MathF.Max(maxX, pixel.X);
            minY = MathF.Min(minY, pixel.Y);
            maxY = MathF.Max(maxY, pixel.Y);
            minZ = MathF.Min(minZ, pixel.Z);
        }

        // Expand outward to whole pixels (so a partially covered pixel still has to occlude), then clip
        // to the buffer: pixels off screen are not visible anyway, so they need not be accounted for.
        double lowX = Math.Max(0, Math.Floor(minX));
        double highX = Math.Min(_width - 1, Math.Ceiling(maxX));
        double lowY = Math.Max(0, Math.Floor(minY));
        double highY = Math.Min(_height - 1, Math.Ceiling(maxY));
        if (!(lowX <= highX) || !(lowY <= highY))
        {
            return false;
        }

        int x0 = (int)lowX, x1 = (int)highX, y0 = (int)lowY, y1 = (int)highY;
        for (int y = y0; y <= y1; y++)
        {
            int row = y * _width;
            for (int x = x0; x <= x1; x++)
            {
                // One pixel without a nearer occluder is enough to keep the candidate.
                if (_depth[row + x] >= minZ)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Estimates how much of the screen a world-space box covers, as a fraction in 0..1, from the
    /// projection of its eight corners. Used to spend a frame's occluder budget on the boxes that
    /// actually block something.
    /// </summary>
    /// <param name="viewProjection">The camera's world-to-clip matrix.</param>
    /// <param name="worldBounds">The box to measure.</param>
    /// <returns>
    /// The covered fraction of the screen, clamped to 0..1. A box straddling the camera plane reports a
    /// full screen (it has no honest projection, and it is certainly not small); one entirely behind the
    /// camera reports nothing.
    /// </returns>
    public static float ScreenAreaFraction(in Matrix4x4 viewProjection, in Aabb3d worldBounds)
    {
        Vector3 min = worldBounds.Min;
        Vector3 max = worldBounds.Max;
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        int inFront = 0;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector4(
                (i & 1) == 0 ? min.X : max.X,
                (i & 2) == 0 ? min.Y : max.Y,
                (i & 4) == 0 ? min.Z : max.Z,
                1f);
            Vector4 clip = Vector4.Transform(corner, viewProjection);
            if (!(clip.W > NearW))
            {
                continue;
            }

            inFront++;
            float inverseW = 1f / clip.W;
            minX = MathF.Min(minX, clip.X * inverseW);
            maxX = MathF.Max(maxX, clip.X * inverseW);
            minY = MathF.Min(minY, clip.Y * inverseW);
            maxY = MathF.Max(maxY, clip.Y * inverseW);
        }

        if (inFront == 0)
        {
            return 0f;
        }

        if (inFront < 8)
        {
            return 1f;
        }

        // Normalized device coordinates span -1..1 on each axis, so the visible screen is 2x2 units.
        float width = Math.Clamp(maxX, -1f, 1f) - Math.Clamp(minX, -1f, 1f);
        float height = Math.Clamp(maxY, -1f, 1f) - Math.Clamp(minY, -1f, 1f);
        if (width <= 0f || height <= 0f || float.IsNaN(width) || float.IsNaN(height))
        {
            return 0f;
        }

        return width * height * 0.25f;
    }

    // Clips one clip-space face against the camera plane and the near plane, then rasterizes what is
    // left. Returns whether any pixel was covered.
    private bool RasterizeClippedFace(ReadOnlySpan<Vector4> face)
    {
        Span<Vector4> polygon = stackalloc Vector4[MaxPolygonVertices];
        Span<Vector4> scratch = stackalloc Vector4[MaxPolygonVertices];

        // w >= NearW keeps the perspective divide meaningful; z + w >= 0 drops what is nearer than the
        // near plane, which only ever costs an occluder some coverage.
        int count = ClipAgainstPlane(face, face.Length, new Vector4(0f, 0f, 0f, 1f), NearW, scratch);
        if (count < 3)
        {
            return false;
        }

        count = ClipAgainstPlane(scratch, count, new Vector4(0f, 0f, 1f, 1f), 0f, polygon);
        if (count < 3)
        {
            return false;
        }

        Span<Vector3> pixels = stackalloc Vector3[MaxPolygonVertices];
        for (int i = 0; i < count; i++)
        {
            pixels[i] = ToPixel(polygon[i]);
        }

        return RasterizePolygon(pixels, count);
    }

    // Sutherland-Hodgman clip of a convex clip-space polygon against dot(v, plane) >= minimum.
    private static int ClipAgainstPlane(ReadOnlySpan<Vector4> source, int count, in Vector4 plane, float minimum, Span<Vector4> destination)
    {
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            Vector4 current = source[i];
            Vector4 next = source[(i + 1) % count];
            float currentDistance = Vector4.Dot(current, plane) - minimum;
            float nextDistance = Vector4.Dot(next, plane) - minimum;
            bool currentInside = currentDistance >= 0f;
            bool nextInside = nextDistance >= 0f;

            if (currentInside && written < destination.Length)
            {
                destination[written++] = current;
            }

            if (currentInside != nextInside && written < destination.Length)
            {
                float t = currentDistance / (currentDistance - nextDistance);
                destination[written++] = current + ((next - current) * t);
            }
        }

        return written;
    }

    // Clip space to buffer pixels plus ndc depth. The y flip is shared by the rasterizer and the
    // candidate test, so the two always agree on which pixel is which.
    private Vector3 ToPixel(in Vector4 clip)
    {
        float inverseW = 1f / clip.W;
        float x = clip.X * inverseW;
        float y = clip.Y * inverseW;
        return new Vector3(
            ((x * 0.5f) + 0.5f) * _width,
            (0.5f - (y * 0.5f)) * _height,
            clip.Z * inverseW);
    }

    // Conservative half-space rasterizer for one convex screen-space polygon. Each vertex is (pixel x,
    // pixel y, ndc depth); depth is linear in screen space, so its gradient comes straight from the
    // polygon's plane. A pixel is written only when every edge function clears half its own gradient —
    // the "shrink by half a pixel" test, which is exactly full coverage — and the value written is the
    // plane's farthest depth within that pixel.
    //
    // A whole face goes through here at once rather than as two triangles: with a conservative coverage
    // test, splitting a quad would leave its shared diagonal unwritten — a one-pixel seam straight
    // across the middle of every occluder, and one unwritten pixel is all it takes to keep a candidate.
    private bool RasterizePolygon(Span<Vector3> vertices, int count)
    {
        if (count < 3 || count > MaxPolygonVertices)
        {
            return false;
        }

        // The shoelace area both rejects degenerate polygons and settles the winding the edge tests
        // below assume (positive, so the interior is on the positive side of every edge).
        double area = 0;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = vertices[i];
            Vector3 q = vertices[(i + 1) % count];
            area += ((double)p.X * q.Y) - ((double)q.X * p.Y);
        }

        if (double.IsNaN(area) || Math.Abs(area) < 1e-9)
        {
            return false;
        }

        if (area < 0)
        {
            vertices.Slice(0, count).Reverse();
        }

        // The depth plane, taken from the widest triangle fanning out of the first vertex: every vertex
        // lies on one plane, so any non-degenerate triple yields the same gradient, but the widest one
        // yields it most accurately.
        Vector3 origin = vertices[0];
        int widest = -1;
        double widestCross = 0;
        for (int i = 1; i + 1 < count; i++)
        {
            Vector3 b = vertices[i];
            Vector3 c = vertices[i + 1];
            double cross = Math.Abs((((double)b.X - origin.X) * ((double)c.Y - origin.Y))
                - (((double)b.Y - origin.Y) * ((double)c.X - origin.X)));
            if (cross > widestCross)
            {
                widestCross = cross;
                widest = i;
            }
        }

        if (widest < 0 || widestCross < 1e-9)
        {
            return false;
        }

        Vector3 u = vertices[widest] - origin;
        Vector3 v = vertices[widest + 1] - origin;
        double normalX = ((double)u.Y * v.Z) - ((double)u.Z * v.Y);
        double normalY = ((double)u.Z * v.X) - ((double)u.X * v.Z);
        double normalZ = ((double)u.X * v.Y) - ((double)u.Y * v.X);
        if (Math.Abs(normalZ) < 1e-12)
        {
            return false;
        }

        double dzdx = -normalX / normalZ;
        double dzdy = -normalY / normalZ;
        if (double.IsNaN(dzdx) || double.IsNaN(dzdy))
        {
            return false;
        }

        // Clamp in double before narrowing: a face clipped only against the near plane can still reach
        // far outside the buffer, well past what an int can hold.
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < count; i++)
        {
            minX = Math.Min(minX, vertices[i].X);
            maxX = Math.Max(maxX, vertices[i].X);
            minY = Math.Min(minY, vertices[i].Y);
            maxY = Math.Max(maxY, vertices[i].Y);
        }

        double lowX = Math.Max(0, Math.Floor(minX));
        double highX = Math.Min(_width - 1, Math.Ceiling(maxX));
        double lowY = Math.Max(0, Math.Floor(minY));
        double highY = Math.Min(_height - 1, Math.Ceiling(maxY));
        if (!(lowX <= highX) || !(lowY <= highY))
        {
            return false;
        }

        int x0 = (int)lowX, x1 = (int)highX, y0 = (int)lowY, y1 = (int)highY;

        // Edge (p, q) as E(x, y) = (q.x - p.x)(y - p.y) - (q.y - p.y)(x - p.x), positive inside for the
        // winding forced above. Stepping x adds dEdx, so the inner loop is one add per edge.
        Span<double> stepX = stackalloc double[MaxPolygonVertices];
        Span<double> stepY = stackalloc double[MaxPolygonVertices];
        Span<double> bias = stackalloc double[MaxPolygonVertices];
        Span<double> edge = stackalloc double[MaxPolygonVertices];
        for (int i = 0; i < count; i++)
        {
            Vector3 p = vertices[i];
            Vector3 q = vertices[(i + 1) % count];
            double dx = (double)q.X - p.X;
            double dy = (double)q.Y - p.Y;
            stepX[i] = -dy;
            stepY[i] = dx;

            // Half the edge function's gradient: clearing it at a pixel's centre means the whole pixel
            // is inside, which is what makes the coverage conservative.
            bias[i] = (0.5 * (Math.Abs(dx) + Math.Abs(dy)))
                + (p.X * stepX[i]) + (p.Y * stepY[i]);
        }

        // Half a pixel of depth slope in each direction: added to the pixel centre it gives the plane's
        // maximum (farthest) depth over the pixel, which is the conservative value to store.
        double depthBias = 0.5 * (Math.Abs(dzdx) + Math.Abs(dzdy));

        bool covered = false;
        for (int y = y0; y <= y1; y++)
        {
            double centreX = x0 + 0.5;
            double centreY = y + 0.5;
            for (int i = 0; i < count; i++)
            {
                edge[i] = (centreX * stepX[i]) + (centreY * stepY[i]) - bias[i];
            }

            double depth = origin.Z + (dzdx * (centreX - origin.X)) + (dzdy * (centreY - origin.Y)) + depthBias;
            int row = y * _width;

            for (int x = x0; x <= x1; x++)
            {
                bool inside = true;
                for (int i = 0; i < count; i++)
                {
                    if (edge[i] < 0)
                    {
                        inside = false;
                        break;
                    }
                }

                if (inside)
                {
                    var value = (float)depth;
                    int index = row + x;
                    if (value < _depth[index])
                    {
                        _depth[index] = value;
                    }

                    covered = true;
                }

                for (int i = 0; i < count; i++)
                {
                    edge[i] += stepX[i];
                }

                depth += dzdx;
            }
        }

        return covered;
    }
}
