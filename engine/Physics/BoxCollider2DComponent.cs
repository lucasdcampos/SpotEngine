using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A rectangular 2D collider. Provides AABB bounds for the legacy solver and a box fixture for the
/// Aether backend. Inherits <see cref="Collider2DComponent.Offset"/>, <see cref="Collider2DComponent.IsTrigger"/>,
/// and <see cref="Collider2DComponent.Layer"/>.
/// </summary>
[ComponentMenu("Box Collider 2D", Order = 50)]
[SceneComponent("BoxCollider2D")]
public class BoxCollider2DComponent : Collider2DComponent
{
    /// <summary>
    /// The full width and height of the box.
    /// </summary>
    public Vector2 Size { get; set; } = Vector2.One;

    /// <summary>
    /// Returns the AABB in world space based on the given entity position.
    /// </summary>
    public Aabb GetWorldBounds(Vector2 position, Vector2 scale)
    {
        return new Aabb(position + Offset * scale, Size * scale);
    }
}
