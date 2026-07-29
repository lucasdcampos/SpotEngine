using System.Numerics;
using Spot.Rendering;

namespace Spot.Physics;

/// <summary>
/// A 2D axis-aligned bounding box, defined by a center and half-extents. Useful for simple overlap
/// tests (for example sprite-vs-sprite collision).
/// </summary>
public readonly struct Aabb
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Aabb"/> struct from a center and full size.
    /// </summary>
    /// <param name="center">The box center.</param>
    /// <param name="size">The full width and height of the box.</param>
    public Aabb(Vector2 center, Vector2 size)
    {
        Center = center;
        HalfExtents = size * 0.5f;
    }

    /// <summary>
    /// Gets the box center.
    /// </summary>
    public Vector2 Center { get; }

    /// <summary>
    /// Gets half the box size along each axis.
    /// </summary>
    public Vector2 HalfExtents { get; }

    /// <summary>
    /// Gets the lower-left corner.
    /// </summary>
    public Vector2 Min => Center - HalfExtents;

    /// <summary>
    /// Gets the upper-right corner.
    /// </summary>
    public Vector2 Max => Center + HalfExtents;

    /// <summary>
    /// Builds a box from a transform, using its XY position and scale. This matches a unit quad
    /// (as drawn for a sprite) placed by the transform.
    /// </summary>
    /// <param name="transform">The transform to build the box from.</param>
    /// <returns>The bounding box.</returns>
    public static Aabb FromTransform(Transform transform) => new(
        new Vector2(transform.Position.X, transform.Position.Y),
        new Vector2(transform.Scale.X, transform.Scale.Y));

    /// <summary>
    /// Returns whether this box overlaps another.
    /// </summary>
    /// <param name="other">The other box.</param>
    /// <returns><see langword="true"/> if the boxes overlap.</returns>
    public bool Intersects(Aabb other) =>
        Math.Abs(Center.X - other.Center.X) <= HalfExtents.X + other.HalfExtents.X
        && Math.Abs(Center.Y - other.Center.Y) <= HalfExtents.Y + other.HalfExtents.Y;

    /// <summary>
    /// Returns whether the box contains a point.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> if the point is inside the box.</returns>
    public bool Contains(Vector2 point) =>
        Math.Abs(point.X - Center.X) <= HalfExtents.X
        && Math.Abs(point.Y - Center.Y) <= HalfExtents.Y;
}
