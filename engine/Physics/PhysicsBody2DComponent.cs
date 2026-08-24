using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A 2D rigid body. Paired with a collider (<see cref="BoxCollider2DComponent"/> or
/// <see cref="CircleCollider2DComponent"/>) it is simulated by the active <see cref="IPhysics2D"/> backend:
/// gravity, collision response, and (on the Aether backend) rotation, friction, and restitution.
/// </summary>
[ComponentMenu("Physics Body 2D", Order = 40)]
[SceneComponent("PhysicsBody2D")]
public class PhysicsBody2DComponent : Component
{
    /// <summary>
    /// Gets or sets the linear velocity. Written back by the backend each step so scripts can read the
    /// simulated velocity, and read by the backend each step so scripts can drive the body.
    /// </summary>
    public Vector2 Velocity { get; set; } = Vector2.Zero;

    /// <summary>
    /// Gets or sets the scale of gravity applied to this body (1 = normal, 0 = weightless).
    /// </summary>
    public float GravityScale { get; set; } = 1.0f;

    /// <summary>Per-second linear velocity damping. Aether backend only.</summary>
    public float LinearDrag { get; set; } = 0.0f;

    /// <summary>
    /// If true, the body is affected by gravity and resolves collisions by moving.
    /// If false, it acts as a static or kinematic body (unaffected by forces).
    /// </summary>
    public bool IsDynamic { get; set; } = true;

    /// <summary>Mass in kilograms. Used by the Aether backend; ignored by the legacy solver.</summary>
    public float Mass { get; set; } = 1.0f;

    /// <summary>Coefficient of friction for contacts. Aether backend only.</summary>
    public float Friction { get; set; } = 0.4f;

    /// <summary>Bounciness: 0 = no bounce, 1 = fully elastic. Aether backend only.</summary>
    public float Restitution { get; set; } = 0.0f;

    /// <summary>
    /// A kinematic body is not pushed by contacts or gravity but still pushes dynamic bodies; move it by
    /// setting its transform. Overrides <see cref="IsDynamic"/> when true. Aether backend only.
    /// </summary>
    public bool IsKinematic { get; set; } = false;

    /// <summary>Locks the body's orientation so it never spins (useful for characters and props). Aether backend only.</summary>
    public bool FreezeRotation { get; set; } = false;

    /// <summary>
    /// Whether the body rested on a surface below it during the last physics step. Set by the active
    /// backend when a floor contact supported the body. Not serialized or shown in the inspector.
    /// </summary>
    internal bool Grounded;
}
