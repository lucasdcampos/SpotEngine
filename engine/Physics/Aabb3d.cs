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
}
