using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// Describes a contact between the entity receiving a collision callback and another entity, passed to
/// <see cref="EntityBehaviour.OnCollisionEnter"/> and its stay/exit counterparts.
/// </summary>
public readonly struct Collision
{
    /// <summary>The other entity involved in the contact.</summary>
    public readonly Entity Other;

    /// <summary>The contact normal in world space, pointing from <see cref="Other"/> toward the entity receiving the callback.</summary>
    public readonly Vector3 Normal;

    /// <summary>A representative contact point in world space.</summary>
    public readonly Vector3 Point;

    /// <summary>Initializes a new collision.</summary>
    public Collision(Entity other, Vector3 normal, Vector3 point)
    {
        Other = other;
        Normal = normal;
        Point = point;
    }
}

/// <summary>
/// One overlapping collidable pair produced by a physics backend for a single step, before it is diffed
/// into enter/stay/exit callbacks by the <see cref="CollisionDispatcher"/>. The normal points from
/// <see cref="B"/> toward <see cref="A"/> (the dispatcher flips it per receiving entity).
/// </summary>
internal readonly struct ContactPair
{
    public readonly Entity A;
    public readonly Entity B;

    /// <summary>Whether either collidable is a trigger, so the pair fires trigger callbacks and generates no physical response.</summary>
    public readonly bool IsTrigger;

    public readonly Vector3 Normal;
    public readonly Vector3 Point;

    public ContactPair(Entity a, Entity b, bool isTrigger, Vector3 normal, Vector3 point)
    {
        A = a;
        B = b;
        IsTrigger = isTrigger;
        Normal = normal;
        Point = point;
    }
}
