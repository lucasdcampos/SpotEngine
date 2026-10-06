using System.Numerics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.Engine;

/// <summary>
/// Shared state for 2D colliders: a local offset plus the trigger flag and collision layer honored by the
/// Aether backend. Concrete colliders — <see cref="BoxCollider2D"/> and
/// <see cref="CircleCollider2D"/> — add their shape. Mirrors <see cref="Collider3D"/>.
/// </summary>
public abstract class Collider2D : Component
{
    // Only the engine's collider shapes derive from this: the physics backends know each concrete shape.
    private protected Collider2D()
    {
    }

    /// <summary>The collider's local offset from the entity's position, scaled by the entity's world scale.</summary>
    public Vector2 Offset { get; set; } = Vector2.Zero;

    /// <summary>
    /// When true, the collider reports overlaps as trigger callbacks (<see cref="Component.OnTriggerEnter"/>)
    /// without producing a physical response, so other bodies pass through it. Aether backend only.
    /// </summary>
    public bool IsTrigger { get; set; }

    /// <summary>
    /// The collision layer (0..30) this collider belongs to. Which layers interact is configured via
    /// <see cref="PhysicsSettings.SetLayerCollision"/>. Aether backend only.
    /// </summary>
    public int Layer { get; set; }
}
