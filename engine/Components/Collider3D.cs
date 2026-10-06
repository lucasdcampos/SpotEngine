using System.Numerics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.Engine;

/// <summary>
/// Shared state for 3D colliders: a local offset plus the trigger flag and collision layer honored by the
/// Bepu backend. Concrete colliders — <see cref="BoxCollider3D"/>,
/// <see cref="SphereCollider3D"/>, and <see cref="CapsuleCollider3D"/> — add their shape.
/// </summary>
public abstract class Collider3D : Component
{
    // Only the engine's collider shapes derive from this: the physics backends know each concrete shape.
    private protected Collider3D()
    {
    }

    /// <summary>The collider's local offset from the entity's position, scaled by the entity's world scale.</summary>
    public Vector3 Offset { get; set; } = Vector3.Zero;

    /// <summary>
    /// When true, the collider reports overlaps as trigger callbacks (<see cref="Component.OnTriggerEnter"/>)
    /// without producing a physical response, so other bodies pass through it. Bepu backend only.
    /// </summary>
    public bool IsTrigger { get; set; }

    /// <summary>
    /// The collision layer (0..31) this collider belongs to. Which layers interact is configured via
    /// <see cref="PhysicsSettings.SetLayerCollision"/>. Bepu backend only.
    /// </summary>
    public int Layer { get; set; }

    /// <summary>
    /// Coefficient of friction for this surface. For static bodies this is the collider value; for dynamic
    /// bodies the <see cref="PhysicsBody3D.Friction"/> takes precedence. Bepu backend only.
    /// </summary>
    [InspectorRange(0.0f, 2.0f, 0.01f)]
    public float Friction { get; set; } = 0.8f;

    /// <summary>
    /// Bounciness: 0 = no bounce, 1 = fully elastic. For static bodies this is the collider value; for
    /// dynamic bodies the <see cref="PhysicsBody3D.Restitution"/> takes precedence. Bepu backend only.
    /// </summary>
    [InspectorRange(0.0f, 1.0f, 0.01f)]
    public float Restitution { get; set; } = 0.0f;
}
