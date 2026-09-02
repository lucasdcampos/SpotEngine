using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A pluggable 2D physics backend for a <see cref="Scene"/>. Mirrors <see cref="IPhysics3D"/>: implementations
/// own whatever simulation state they need, are created lazily the first time a scene runs in play mode, then
/// disposed when the scene exits. The engine ships two: <see cref="Aether.AetherPhysics2D"/> (default, a real
/// rigid-body simulation) and <see cref="LegacyPhysics2D"/> (the built-in AABB solver). Selected via
/// <see cref="PhysicsSettings.Backend2D"/>.
/// </summary>
internal interface IPhysics2D : IDisposable
{
    /// <summary>
    /// Advances the simulation one step: syncs authoring components into the backend, integrates,
    /// and writes results back onto the entities' transforms. Must never throw (log and continue).
    /// </summary>
    void Step(Scene scene, float deltaTime);

    /// <summary>
    /// Casts a ray against the simulation in the XY plane. Returns <see langword="true"/> and fills
    /// <paramref name="hit"/> with the closest intersection within <paramref name="maxDistance"/>,
    /// otherwise <see langword="false"/>.
    /// </summary>
    bool Raycast(Scene scene, Vector2 origin, Vector2 direction, float maxDistance, out RaycastHit2D hit);

    /// <summary>
    /// The overlapping collidable pairs detected during the most recent <see cref="Step"/>, consumed by
    /// the <see cref="CollisionDispatcher"/> to raise enter/stay/exit callbacks. A backend that does not
    /// report contacts returns an empty list.
    /// </summary>
    IReadOnlyList<ContactPair> Contacts { get; }
}

/// <summary>
/// The result of a successful <see cref="Scene.Raycast2D(Vector2, Vector2, float, out RaycastHit2D)"/>.
/// </summary>
public readonly struct RaycastHit2D
{
    /// <summary>The entity whose collider was hit.</summary>
    public readonly Entity Entity;

    /// <summary>The world-space point of intersection, in the XY plane.</summary>
    public readonly Vector2 Point;

    /// <summary>The surface normal at the intersection.</summary>
    public readonly Vector2 Normal;

    /// <summary>The distance from the ray origin to the hit point.</summary>
    public readonly float Distance;

    public RaycastHit2D(Entity entity, Vector2 point, Vector2 normal, float distance)
    {
        Entity = entity;
        Point = point;
        Normal = normal;
        Distance = distance;
    }
}
