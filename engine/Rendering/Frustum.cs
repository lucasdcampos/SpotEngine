using System;
using System.Numerics;
using Spot.Physics;

namespace Spot.Rendering;

/// <summary>
/// The six clipping planes of a view-projection volume, used for coarse visibility culling: an object
/// whose bounds fall entirely outside any plane can be skipped before it ever reaches a draw call.
/// </summary>
/// <remarks>
/// The planes are extracted straight from a view-projection matrix with the Gribb/Hartmann method
/// (sums and differences of the matrix's clip-space rows), so any matrix that maps world space to
/// OpenGL clip space — a camera's view-projection, or a light's shadow matrix — yields a usable
/// frustum. Each plane's normal points <b>inward</b>; a point is inside the frustum when it lies on
/// the positive side of all six. The test is deliberately conservative: it never culls something that
/// is actually visible, at the cost of occasionally keeping something just out of view.
/// </remarks>
public readonly struct Frustum
{
    // Six inward-facing planes as (a, b, c, d) where a*x + b*y + c*z + d >= 0 inside. Order: left,
    // right, bottom, top, near, far. Left unnormalized — the AABB test only checks a sign, which is
    // invariant under the common positive scale of a plane's coefficients.
    private readonly Vector4 _left;
    private readonly Vector4 _right;
    private readonly Vector4 _bottom;
    private readonly Vector4 _top;
    private readonly Vector4 _near;
    private readonly Vector4 _far;

    /// <summary>
    /// Builds a frustum from a view-projection matrix (or any world-to-clip matrix, such as a light's
    /// shadow matrix).
    /// </summary>
    /// <param name="m">The matrix mapping world space to clip space.</param>
    public Frustum(Matrix4x4 m)
    {
        // Columns of the matrix under System.Numerics' row-vector convention (clip = point * m), so
        // clip.x = dot(point, col0), clip.w = dot(point, col3). The clip-space inequalities
        // -w <= x <= w (and likewise y, z) become the six planes below.
        Vector4 col0 = new(m.M11, m.M21, m.M31, m.M41);
        Vector4 col1 = new(m.M12, m.M22, m.M32, m.M42);
        Vector4 col2 = new(m.M13, m.M23, m.M33, m.M43);
        Vector4 col3 = new(m.M14, m.M24, m.M34, m.M44);

        _left = col3 + col0;   // x + w >= 0
        _right = col3 - col0;  // w - x >= 0
        _bottom = col3 + col1; // y + w >= 0
        _top = col3 - col1;    // w - y >= 0
        _near = col3 + col2;   // z + w >= 0
        _far = col3 - col2;    // w - z >= 0
    }

    /// <summary>
    /// Tests whether an axis-aligned box (in the same space the frustum was built in) is at least
    /// partially inside the frustum.
    /// </summary>
    /// <param name="box">The world-space bounds to test.</param>
    /// <returns>
    /// <see langword="false"/> only when the box lies wholly outside one of the planes (definitely not
    /// visible); <see langword="true"/> otherwise. A degenerate (all-zero) matrix keeps everything.
    /// </returns>
    public bool Intersects(in Aabb3d box)
    {
        Vector3 c = box.Center;
        Vector3 h = box.HalfExtents;
        return !Outside(_left, c, h)
            && !Outside(_right, c, h)
            && !Outside(_bottom, c, h)
            && !Outside(_top, c, h)
            && !Outside(_near, c, h)
            && !Outside(_far, c, h);
    }

    // The box is fully behind a plane when its center's signed distance, plus the box's projected
    // radius onto the plane normal, is still negative.
    private static bool Outside(in Vector4 plane, Vector3 center, Vector3 half)
    {
        float distance = plane.X * center.X + plane.Y * center.Y + plane.Z * center.Z + plane.W;
        float radius = Math.Abs(plane.X) * half.X + Math.Abs(plane.Y) * half.Y + Math.Abs(plane.Z) * half.Z;
        return distance + radius < 0f;
    }
}
