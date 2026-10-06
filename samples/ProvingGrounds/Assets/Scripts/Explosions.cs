using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>
/// Blasts: everything shootable in range takes damage that falls off with distance, every dynamic body gets an
/// impulse pushing it away — applied a little off its center so it tumbles — and a player caught in one is thrown,
/// which is how rocket jumps work.
/// </summary>
public static class Explosions
{
    /// <summary>Detonates at a point and returns the strongest hit it dealt.</summary>
    /// <param name="scene">The scene to blast.</param>
    /// <param name="center">Where it goes off.</param>
    /// <param name="radius">How far it reaches.</param>
    /// <param name="damage">The damage at the center.</param>
    /// <param name="impulse">The push at the center, in newton-seconds.</param>
    public static HitResult Detonate(Scene scene, Vector3 center, float radius, float damage, float impulse)
    {
        Effects.Current?.Explosion(center, radius);
        Playground? game = Playground.Current;
        HitResult best = HitResult.None;

        foreach (IShootable target in scene.GetComponents<IShootable>())
        {
            Vector3 point = target.AimPoint;
            float distance = Vector3.Distance(point, center);
            if (distance > radius) continue;

            float falloff = 1.0f - distance / radius;
            Vector3 away = distance > 1e-3f ? (point - center) / distance : Vector3.UnitY;
            HitResult result = target.OnShot(new ShotInfo(point, -away, away, damage * (0.4f + 0.6f * falloff), DamageKind.Explosion));
            if (result > best) best = result;
        }

        foreach (Entity entity in scene.View<PhysicsBody3D>())
        {
            if (!entity.IsActiveInHierarchy() || (game is not null && entity == game.Player)) continue;

            PhysicsBody3D body = entity.GetComponent<PhysicsBody3D>();
            if (!body.Enabled || !body.IsDynamic || body.IsKinematic) continue;

            Vector3 position = entity.GetComponent<Transform>().WorldPosition;
            float distance = Vector3.Distance(position, center);
            if (distance > radius) continue;

            float falloff = 1.0f - distance / radius;
            Vector3 away = Vector3.Normalize(position - center + new Vector3(0.0f, 0.6f, 0.0f));
            float mass = MathF.Max(0.1f, body.Mass);
            float speed = MathF.Min(impulse * falloff * falloff / mass, 22.0f);
            Vector3 offset = new Vector3(Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f, Random.Shared.NextSingle() - 0.5f) * 0.3f;
            body.AddImpulseAtPosition(away * speed * mass, position - away * 0.15f + offset);
        }

        if (game is not null)
        {
            ThrowPlayer(game, center, radius);
            if (best != HitResult.None) game.RegisterHit(best);
        }

        return best;
    }

    // A blast lifts the player: from below it is a rocket jump.
    private static void ThrowPlayer(Playground game, Vector3 center, float radius)
    {
        if (!game.Player.IsValid || !game.Player.TryGetComponent(out PhysicsBody3D? body)) return;

        Vector3 chest = game.Player.GetComponent<Transform>().Position + new Vector3(0.0f, 0.9f, 0.0f);
        float distance = Vector3.Distance(chest, center);
        float reach = radius * 0.9f;
        if (distance > reach) return;

        float falloff = 1.0f - distance / reach;
        Vector3 away = distance > 1e-3f ? (chest - center) / distance : Vector3.UnitY;
        away = Vector3.Normalize(away + new Vector3(0.0f, 0.35f, 0.0f));
        Vector3 velocity = body.Velocity + away * 19.0f * falloff;
        if (Player.Current is { } player && player.Entity == game.Player)
        {
            player.Launch(velocity);
        }
        else
        {
            body.Velocity = velocity;
        }

        if (falloff > 0.3f && center.Y < chest.Y - 0.4f)
        {
            game.Stats.RocketJumps++;
            game.PushFeed("Rocket jump", "", HudColors.Warm);
        }
    }
}
