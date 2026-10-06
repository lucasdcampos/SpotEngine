using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.Engine.Physics;

/// <summary>
/// A circular 2D collider for the Aether backend. Inherits <see cref="Collider2DComponent.Offset"/>,
/// <see cref="Collider2DComponent.IsTrigger"/>, and <see cref="Collider2DComponent.Layer"/>. The radius is
/// scaled by the entity's world X scale. The legacy AABB solver ignores this collider (Aether backend only).
/// </summary>
[ComponentMenu("Circle Collider 2D", Order = 51, Category = "Physics 2D")]
[SceneComponent("CircleCollider2D")]
public sealed class CircleCollider2DComponent : Collider2DComponent
{
    /// <summary>The radius of the circle, in local units (scaled by the entity's world scale).</summary>
    public float Radius { get; set; } = 0.5f;
}
