using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Framework.Mathematics;

namespace Spot.Engine.Physics;

/// <summary>
/// A component to define a simple 3D box for AABB collisions.
/// </summary>
[ComponentMenu("Box Collider 3D", Order = 70, Category = "Physics")]
[SceneComponent("BoxCollider3D")]
public sealed class BoxCollider3DComponent : Collider3DComponent
{
    public Vector3 Size { get; set; } = Vector3.One;

    public Aabb3d GetWorldBounds(Vector3 position, Vector3 scale)
    {
        return new Aabb3d(position + Offset * scale, Size * scale);
    }
}
