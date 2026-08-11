using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A component to define a simple 3D box for AABB collisions.
/// </summary>
[ComponentMenu("Box Collider 3D", Order = 70)]
[SceneComponent("BoxCollider3D")]
public class BoxCollider3DComponent : Collider3DComponent
{
    public Vector3 Size { get; set; } = Vector3.One;

    public Aabb3d GetWorldBounds(Vector3 position, Vector3 scale)
    {
        return new Aabb3d(position + Offset * scale, Size * scale);
    }
}
