using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A 3D rigid body. Paired with a collider (<see cref="BoxCollider3DComponent"/>,
/// <see cref="SphereCollider3DComponent"/>, or <see cref="CapsuleCollider3DComponent"/>) it is
/// simulated by the active <see cref="IPhysics3D"/> backend: gravity, collision response, and
/// (on the Bepu backend) rotation, friction, and restitution.
/// </summary>
[ComponentMenu("Physics Body 3D", Order = 60)]
[SceneComponent("PhysicsBody3D")]
public class PhysicsBody3DComponent : Component
{
    public Vector3 Velocity { get; set; } = Vector3.Zero;
    public float GravityScale { get; set; } = 1.0f;
    public float LinearDrag { get; set; } = 0.0f;

    /// <summary>Whether the body is simulated (affected by gravity and forces). A non-dynamic body acts as immovable geometry.</summary>
    public bool IsDynamic { get; set; } = true;

    /// <summary>Mass in kilograms. Used by the Bepu backend to compute inertia; ignored by the legacy solver.</summary>
    public float Mass { get; set; } = 1.0f;

    /// <summary>Coefficient of friction for contacts. Bepu backend only.</summary>
    public float Friction { get; set; } = 0.8f;

    /// <summary>Bounciness: 0 = no bounce, 1 = fully elastic. Bepu backend only.</summary>
    public float Restitution { get; set; } = 0.0f;

    /// <summary>
    /// A kinematic body is not pushed by contacts or gravity but still pushes dynamic bodies; move it by
    /// setting its transform. Overrides <see cref="IsDynamic"/> when true. Bepu backend only.
    /// </summary>
    public bool IsKinematic { get; set; } = false;

    /// <summary>Locks the body's orientation so it never tips over (useful for characters and props). Bepu backend only.</summary>
    public bool FreezeRotation { get; set; } = false;

    /// <summary>
    /// Whether the body rested on a surface below it during the last physics step. Set by the active
    /// backend when a floor contact supported the body, and read by the character controller for a
    /// reliable grounded test. Not serialized or shown in the inspector.
    /// </summary>
    internal bool Grounded;
}
