using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.Engine;

/// <summary>
/// A circular 2D collider for the Aether backend. Inherits <see cref="Collider2D.Offset"/>,
/// <see cref="Collider2D.IsTrigger"/>, and <see cref="Collider2D.Layer"/>. The radius is
/// scaled by the entity's world X scale. The legacy AABB solver ignores this collider (Aether backend only).
/// </summary>
[ComponentMenu("Circle Collider 2D", Order = 51, Category = "Physics 2D")]
[SceneComponent("CircleCollider2D")]
public sealed class CircleCollider2D : Collider2D
{
    /// <summary>The radius of the circle, in local units (scaled by the entity's world scale).</summary>
    public float Radius { get; set; } = 0.5f;
}
