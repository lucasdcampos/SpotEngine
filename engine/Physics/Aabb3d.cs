using System;
using System.Numerics;
using Spot.Rendering;

namespace Spot.Physics;

/// <summary>
/// A 3D axis-aligned bounding box.
/// </summary>
public readonly struct Aabb3d
{
    public Aabb3d(Vector3 center, Vector3 size)
    {
        Center = center;
        HalfExtents = size * 0.5f;
    }

    public Vector3 Center { get; }
    public Vector3 HalfExtents { get; }
    public Vector3 Min => Center - HalfExtents;
    public Vector3 Max => Center + HalfExtents;

    public bool Intersects(Aabb3d other) =>
        Math.Abs(Center.X - other.Center.X) <= HalfExtents.X + other.HalfExtents.X
        && Math.Abs(Center.Y - other.Center.Y) <= HalfExtents.Y + other.HalfExtents.Y
        && Math.Abs(Center.Z - other.Center.Z) <= HalfExtents.Z + other.HalfExtents.Z;

    public bool Contains(Vector3 point) =>
        Math.Abs(point.X - Center.X) <= HalfExtents.X
        && Math.Abs(point.Y - Center.Y) <= HalfExtents.Y
        && Math.Abs(point.Z - Center.Z) <= HalfExtents.Z;

    /// <summary>
    /// Maps this box through an affine transform, returning the tightest axis-aligned box that still
    /// contains it. The center is transformed as a point; each half-extent axis grows by the absolute
    /// value of the matrix's rotation/scale rows, which is the standard AABB transform (no 8-corner
    /// loop). A non-affine (projective) matrix is not supported — feed world matrices only.
    /// </summary>
    /// <param name="m">The affine transform to apply (typically an entity's world matrix).</param>
    /// <returns>The transformed axis-aligned box.</returns>
    public Aabb3d Transform(in Matrix4x4 m)
    {
        Vector3 center = Vector3.Transform(Center, m);
        Vector3 extents = new Vector3(
            Math.Abs(m.M11) * HalfExtents.X + Math.Abs(m.M21) * HalfExtents.Y + Math.Abs(m.M31) * HalfExtents.Z,
            Math.Abs(m.M12) * HalfExtents.X + Math.Abs(m.M22) * HalfExtents.Y + Math.Abs(m.M32) * HalfExtents.Z,
            Math.Abs(m.M13) * HalfExtents.X + Math.Abs(m.M23) * HalfExtents.Y + Math.Abs(m.M33) * HalfExtents.Z);

        // Aabb3d's constructor halves the size argument, so pass full extents (2 * half).
        return new Aabb3d(center, extents * 2f);
    }

    /// <summary>
    /// Returns a copy of this box with its half-extents scaled about the same center. Used to pad a
    /// mesh's bind-pose bounds so animation never moves geometry outside the tested volume.
    /// </summary>
    /// <param name="factor">The multiplier applied to the half-extents.</param>
    /// <returns>The padded box.</returns>
    public Aabb3d Expanded(float factor) => new Aabb3d(Center, HalfExtents * 2f * factor);
}
