using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;

namespace Spot.Physics.Bepu;

/// <summary>Records the closest collidable a ray intersects.</summary>
internal struct ClosestRayHandler : IRayHitHandler
{
    public bool Hit;
    public float T;
    public Vector3 Normal;
    public CollidableReference Collidable;

    public readonly bool AllowTest(CollidableReference collidable) => true;
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

/// <summary>Reports whether a downward ray hits anything other than the probing body itself.</summary>
internal struct GroundRayHandler : IRayHitHandler
{
    private readonly int _ignoreBody;
    public bool Hit;

    public GroundRayHandler(int ignoreBody) => _ignoreBody = ignoreBody;

    public readonly bool AllowTest(CollidableReference collidable)
        => collidable.Mobility == CollidableMobility.Static || collidable.BodyHandle.Value != _ignoreBody;

    public readonly bool AllowTest(CollidableReference collidable, int childIndex) => true;

    public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
    {
        maximumT = t;
        Hit = true;
    }
}
