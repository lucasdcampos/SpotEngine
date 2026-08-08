using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A capsule collider (an upright cylinder capped by hemispheres) for 3D physics. Ideal for
/// characters. <see cref="Length"/> is the straight cylindrical section between the two caps, so the
/// total height is <c>Length + 2 * Radius</c>. Simulated by the Bepu backend; the legacy AABB solver
/// ignores it.
/// </summary>
[ComponentMenu("Capsule Collider 3D", Order = 72)]
[SceneComponent("CapsuleCollider3D")]
public class CapsuleCollider3DComponent : Component
{
    public float Radius { get; set; } = 0.3f;
    public float Length { get; set; } = 1.0f;
    public Vector3 Offset { get; set; } = Vector3.Zero;

    /// <summary>
    /// When true, the collider reports overlaps as trigger callbacks (<see cref="EntityBehaviour.OnTriggerEnter"/>)
    /// without producing a physical response, so other bodies pass through it. Bepu backend only.
    /// </summary>
    public bool IsTrigger { get; set; }
}
