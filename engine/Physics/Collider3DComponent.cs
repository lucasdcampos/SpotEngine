using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// Shared state for 3D colliders: a local offset plus the trigger flag and collision layer honored by the
/// Bepu backend. Concrete colliders — <see cref="BoxCollider3DComponent"/>,
/// <see cref="SphereCollider3DComponent"/>, and <see cref="CapsuleCollider3DComponent"/> — add their shape.
/// </summary>
public abstract class Collider3DComponent : Component
{
    /// <summary>The collider's local offset from the entity's position, scaled by the entity's world scale.</summary>
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
}
