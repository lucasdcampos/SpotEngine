using System;
using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A component to define a simple rectangle for AABB collisions.
/// </summary>
[ComponentMenu("Box Collider 2D", Order = 50)]
[SceneComponent("BoxCollider2D")]
public class BoxCollider2DComponent : Component
{
    /// <summary>
    /// The full width and height of the box.
    /// </summary>
    public Vector2 Size { get; set; } = Vector2.One;

    /// <summary>
    /// The offset from the entity's position to the center of the box.
    /// </summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// Returns the AABB in world space based on the given entity position.
    /// </summary>
    public Aabb GetWorldBounds(Vector2 position, Vector2 scale)
    {
        return new Aabb(position + Offset * scale, Size * scale);
    }
}
