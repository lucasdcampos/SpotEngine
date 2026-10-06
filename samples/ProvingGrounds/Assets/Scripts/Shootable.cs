using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>How a hit was dealt.</summary>
public enum DamageKind
{
    Bullet,
    Explosion,
}

/// <summary>What a hit did, which the HUD's hit marker shows.</summary>
public enum HitResult
{
    None,
    Hit,
    Kill,
}

/// <summary>One hit on something shootable: where it landed, which way it travelled, and how hard it was.</summary>
public readonly record struct ShotInfo(Vector3 Point, Vector3 Normal, Vector3 Direction, float Damage, DamageKind Kind);

/// <summary>
/// Something that reacts to being shot — a target, a drone, an explosive crate, the challenge's orbs. A script
/// implements it; the weapons find it on the collider they hit or on one of its parents.
/// </summary>
public interface IShootable
{
    /// <summary>Gets the point an explosion measures its distance to.</summary>
    Vector3 AimPoint { get; }

    /// <summary>Handles a hit and says what it did.</summary>
    HitResult OnShot(in ShotInfo shot);
}

/// <summary>The collision layers the game puts colliders on (see <see cref="Playground"/> for the matrix).</summary>
public static class Layers
{
    /// <summary>Level geometry, props, targets.</summary>
    public const int World = 0;

    /// <summary>The player's capsule: the weapons' rays start inside it, so they leave this layer out.</summary>
    public const int Player = 1;

    /// <summary>Launcher grenades: they never hit the player who fires them, or each other.</summary>
    public const int Projectile = 2;

    /// <summary>Crate shards: they tumble on the world but don't trip the player up.</summary>
    public const int Debris = 3;

    /// <summary>What a bullet can hit: everything but the shooter and grenades in flight.</summary>
    public static uint Shots => PhysicsSettings.AllLayers & ~(1u << Player) & ~(1u << Projectile);
}
