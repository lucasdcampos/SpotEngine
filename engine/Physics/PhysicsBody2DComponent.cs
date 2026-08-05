using System;
using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A component to handle velocity and gravity for 2D physics.
/// </summary>
[ComponentMenu("Physics Body 2D", Order = 40)]
[SceneComponent("PhysicsBody2D")]
public class PhysicsBody2DComponent : Component
{
    /// <summary>
    /// Gets or sets the linear velocity.
    /// </summary>
    public Vector2 Velocity { get; set; } = Vector2.Zero;

    /// <summary>
    /// Gets or sets the scale of gravity applied to this body.
    /// </summary>
    public float GravityScale { get; set; } = 1.0f;

    /// <summary>
    /// If true, the body is affected by gravity and resolves collisions by moving.
    /// If false, it acts as a static or kinematic body (unaffected by forces).
    /// </summary>
    public bool IsDynamic { get; set; } = true;
}
