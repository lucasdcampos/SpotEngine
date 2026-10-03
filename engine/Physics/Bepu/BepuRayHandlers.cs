using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;

namespace Spot.Engine.Physics.Bepu;

/// <summary>
/// Records the closest collidable a ray intersects, among those on a layer in the mask (and, unless asked for,
/// skipping triggers).
/// </summary>
internal struct ClosestRayHandler : IRayHitHandler
{
    private readonly BepuContacts _contacts;
    private readonly uint _layerMask;
    private readonly bool _hitTriggers;

    public bool Hit;
    public float T;
    public Vector3 Normal;
    public CollidableReference Collidable;

    public ClosestRayHandler(BepuContacts contacts, uint layerMask, bool hitTriggers)
    {
        _contacts = contacts;
        _layerMask = layerMask;
        _hitTriggers = hitTriggers;
    }

    public readonly bool AllowTest(CollidableReference collidable) =>
        (_layerMask & (1u << _contacts.LayerOf(collidable))) != 0 && (_hitTriggers || !_contacts.IsTrigger(collidable));
    public readonly bool AllowTest(CollidableReference collidable, int childIndex) => true;

    public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
    {
        // Shrinking maximumT means later callbacks only fire for closer hits, so the last recorded wins.
        maximumT = t;
        Hit = true;
        T = t;
        Normal = normal;
        Collidable = collidable;
    }
}

/// <summary>
/// Reports whether a downward ray hits solid ground: anything but the probing body itself and trigger volumes, which a
/// character stands in, not on.
/// </summary>
internal struct GroundRayHandler : IRayHitHandler
{
    private readonly int _ignoreBody;
    private readonly BepuContacts _contacts;
    public bool Hit;

    public GroundRayHandler(int ignoreBody, BepuContacts contacts)
    {
        _ignoreBody = ignoreBody;
        _contacts = contacts;
    }

    public readonly bool AllowTest(CollidableReference collidable)
        => (collidable.Mobility == CollidableMobility.Static || collidable.BodyHandle.Value != _ignoreBody) && !_contacts.IsTrigger(collidable);

    public readonly bool AllowTest(CollidableReference collidable, int childIndex) => true;

    public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
    {
        maximumT = t;
        Hit = true;
    }
}
