using System.Numerics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.Engine;

/// <summary>
/// A spherical collider for 3D physics. Simulated by the Bepu backend; the legacy AABB solver
/// ignores it.
/// </summary>
[ComponentMenu("Sphere Collider 3D", Order = 71, Category = "Physics")]
[SceneComponent("SphereCollider3D")]
public sealed class SphereCollider3D : Collider3D
{
    public float Radius { get; set; } = 0.5f;
}
