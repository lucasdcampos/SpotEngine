using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A component to handle velocity and gravity for 3D physics.
/// </summary>
[ComponentMenu("Physics Body 3D", Order = 60)]
public class PhysicsBody3DComponent : Component
{
    public Vector3 Velocity { get; set; } = Vector3.Zero;
    public float GravityScale { get; set; } = 1.0f;
    public float LinearDrag { get; set; } = 0.0f;
    public bool IsDynamic { get; set; } = true;

    /// <summary>
    /// Whether the body rested on a surface below it during the last physics step. Set by
    /// <see cref="Physics3DSystem"/> when a floor contact pushed the body up, and read by the
    /// character controller for a reliable grounded test. Not serialized or shown in the inspector.
    /// </summary>
    internal bool Grounded;
}
