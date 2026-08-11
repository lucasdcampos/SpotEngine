using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A spherical collider for 3D physics. Simulated by the Bepu backend; the legacy AABB solver
/// ignores it.
/// </summary>
[ComponentMenu("Sphere Collider 3D", Order = 71)]
[SceneComponent("SphereCollider3D")]
public class SphereCollider3DComponent : Collider3DComponent
{
    public float Radius { get; set; } = 0.5f;
}
