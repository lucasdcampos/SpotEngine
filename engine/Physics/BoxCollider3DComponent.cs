using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A component to define a simple 3D box for AABB collisions.
/// </summary>
[ComponentMenu("Box Collider 3D", Order = 70)]
[SceneComponent("BoxCollider3D")]
public class BoxCollider3DComponent : Component
{
    public Vector3 Size { get; set; } = Vector3.One;
    public Vector3 Offset { get; set; } = Vector3.Zero;

    /// <summary>
    /// When true, the collider reports overlaps as trigger callbacks (<see cref="EntityBehaviour.OnTriggerEnter"/>)
    /// without producing a physical response, so other bodies pass through it. Bepu backend only.
    /// </summary>
    public bool IsTrigger { get; set; }

    /// <summary>
    /// The collision layer (0..31) this collider belongs to. Which layers interact is configured via
    /// <see cref="PhysicsSettings.SetLayerCollision"/>. Bepu backend only.
    /// </summary>
    public int Layer { get; set; }

    public Aabb3d GetWorldBounds(Vector3 position, Vector3 scale)
    {
        return new Aabb3d(position + Offset * scale, Size * scale);
    }
}
