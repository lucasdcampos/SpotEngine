using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A component to define a simple 3D box for AABB collisions.
/// </summary>
[ComponentMenu("Box Collider 3D", Order = 70)]
public class BoxCollider3DComponent : Component
{
    public Vector3 Size { get; set; } = Vector3.One;
    public Vector3 Offset { get; set; } = Vector3.Zero;

    public Aabb3d GetWorldBounds(Vector3 position)
    {
        return new Aabb3d(position + Offset, Size);
    }
}
