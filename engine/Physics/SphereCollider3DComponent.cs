using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A spherical collider for 3D physics. Simulated by the Bepu backend; the legacy AABB solver
/// ignores it.
/// </summary>
[ComponentMenu("Sphere Collider 3D", Order = 71)]
[SceneComponent("SphereCollider3D")]
public class SphereCollider3DComponent : Component
{
    public float Radius { get; set; } = 0.5f;
    public Vector3 Offset { get; set; } = Vector3.Zero;

    /// <summary>
    /// When true, the collider reports overlaps as trigger callbacks (<see cref="EntityBehaviour.OnTriggerEnter"/>)
    /// without producing a physical response, so other bodies pass through it. Bepu backend only.
    /// </summary>
    public bool IsTrigger { get; set; }
}
