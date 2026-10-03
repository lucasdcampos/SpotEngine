using System.Numerics;
using Spot.Engine.Scenes;

namespace Spot.Engine.Physics;

/// <summary>
/// A capsule collider (an upright cylinder capped by hemispheres) for 3D physics. Ideal for
/// characters. <see cref="Length"/> is the straight cylindrical section between the two caps, so the
/// total height is <c>Length + 2 * Radius</c>. Simulated by the Bepu backend; the legacy AABB solver
/// ignores it.
/// </summary>
[ComponentMenu("Capsule Collider 3D", Order = 72, Category = "Physics")]
[SceneComponent("CapsuleCollider3D")]
public sealed class CapsuleCollider3DComponent : Collider3DComponent
{
    public float Radius { get; set; } = 0.3f;
    public float Length { get; set; } = 1.0f;
}
