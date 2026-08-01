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
    public bool IsDynamic { get; set; } = true;
}
