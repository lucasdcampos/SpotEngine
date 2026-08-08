using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A pluggable 3D physics backend for a <see cref="Scene"/>. Implementations own whatever simulation
/// state they need and are created lazily the first time a scene runs in play mode, then disposed when
/// the scene exits. The engine ships two: <see cref="Bepu.BepuPhysics3D"/> (default) and
/// <see cref="LegacyPhysics3D"/> (the built-in AABB solver). Selected via <see cref="PhysicsSettings.Backend"/>.
/// </summary>
internal interface IPhysics3D : IDisposable
{
    /// <summary>
    /// Advances the simulation one step: syncs authoring components into the backend, integrates,
    /// and writes results back onto the entities' transforms. Must never throw (log and continue).
    /// </summary>
    void Step(Scene scene, float deltaTime);

    /// <summary>
    /// Casts a ray against the simulation. Returns <see langword="true"/> and fills <paramref name="hit"/>
    /// with the closest intersection within <paramref name="maxDistance"/>, otherwise <see langword="false"/>.
    /// </summary>
    bool Raycast(Scene scene, Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit);

    /// <summary>
    /// The overlapping collidable pairs detected during the most recent <see cref="Step"/>, consumed by
    /// the <see cref="CollisionDispatcher"/> to raise enter/stay/exit callbacks. A backend that does not
    /// report contacts returns an empty list.
    /// </summary>
    IReadOnlyList<ContactPair> Contacts { get; }
}

/// <summary>
/// The result of a successful <see cref="Scene.Raycast(Vector3, Vector3, float, out RaycastHit)"/>.
/// </summary>
public readonly struct RaycastHit
{
    /// <summary>The entity whose collider was hit.</summary>
    public readonly Entity Entity;

    /// <summary>The world-space point of intersection.</summary>
    public readonly Vector3 Point;

    /// <summary>The surface normal at the intersection.</summary>
    public readonly Vector3 Normal;

    /// <summary>The distance from the ray origin to the hit point.</summary>
    public readonly float Distance;

    public RaycastHit(Entity entity, Vector3 point, Vector3 normal, float distance)
    {
        Entity = entity;
        Point = point;
        Normal = normal;
        Distance = distance;
    }
}
