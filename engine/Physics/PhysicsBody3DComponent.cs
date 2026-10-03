using System.Numerics;
using Spot.Engine.Scenes;

namespace Spot.Engine.Physics;

/// <summary>
/// A 3D rigid body. Paired with a collider (<see cref="BoxCollider3DComponent"/>,
/// <see cref="SphereCollider3DComponent"/>, or <see cref="CapsuleCollider3DComponent"/>) it is
/// simulated by the active <see cref="IPhysics3D"/> backend: gravity, collision response, and
/// (on the Bepu backend) rotation, friction, and restitution.
/// </summary>
[ComponentMenu("Physics Body 3D", Order = 60)]
[SceneComponent("PhysicsBody3D")]
public sealed class PhysicsBody3DComponent : Component
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

    // Impulses queued since the last step, applied by the backend when it next pushes this body into the
    // simulation. Point impulses beyond the cap fold into the linear sum so a runaway caller can't grow the list.
    private const int MaxPointImpulses = 64;
    private readonly List<(Vector3 Impulse, Vector3 Position)> _pointImpulses = new();
    private Vector3 _linearImpulse;

    /// <summary>
    /// Applies an instantaneous push through the body's center of mass at the next physics step: its velocity
    /// changes by <paramref name="impulse"/> divided by its <see cref="Mass"/>. Only dynamic bodies respond; the
    /// impulse is dropped for static and kinematic ones.
    /// </summary>
    /// <param name="impulse">The impulse in world space (newton-seconds: mass times change in velocity).</param>
    public void AddImpulse(Vector3 impulse) => _linearImpulse += impulse;

    /// <summary>
    /// Applies an instantaneous push at a world-space point at the next physics step — a bullet hitting the edge
    /// of a crate — so the body both moves and, unless <see cref="FreezeRotation"/> is set, starts spinning. Only
    /// dynamic bodies respond. The legacy backend has no rotation and applies just the linear part.
    /// </summary>
    /// <param name="impulse">The impulse in world space (newton-seconds).</param>
    /// <param name="position">The world-space point the impulse acts at.</param>
    public void AddImpulseAtPosition(Vector3 impulse, Vector3 position)
    {
        if (_pointImpulses.Count < MaxPointImpulses)
        {
            _pointImpulses.Add((impulse, position));
        }
        else
        {
            _linearImpulse += impulse;
        }
    }

    internal bool HasPendingImpulses => _linearImpulse != Vector3.Zero || _pointImpulses.Count > 0;

    internal IReadOnlyList<(Vector3 Impulse, Vector3 Position)> PendingPointImpulses => _pointImpulses;

    internal Vector3 PendingCentralImpulse => _linearImpulse;

    /// <summary>The sum of every pending impulse, central and at a point: its effect on the linear velocity.</summary>
    internal Vector3 PendingLinearImpulse()
    {
        Vector3 total = _linearImpulse;
        foreach ((Vector3 impulse, Vector3 _) in _pointImpulses)
        {
            total += impulse;
        }

        return total;
    }

    internal void ClearPendingImpulses()
    {
        _linearImpulse = Vector3.Zero;
        _pointImpulses.Clear();
    }
}
