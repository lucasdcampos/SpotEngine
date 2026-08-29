using System.Numerics;
using Spot.Scenes;

namespace Spot.Game;

/// <summary>
/// A bullet fired by the player. It travels in a straight line, expires after a short lifetime, is
/// stopped by obstacles, and damages the first enemy it overlaps (spawning an impact spark). Spawned at
/// runtime by <see cref="PlayerController"/>, which seeds <see cref="Direction"/> and <see cref="Speed"/>.
/// </summary>
public sealed class Projectile : EntityBehaviour
{
    /// <summary>The normalized travel direction in the XY plane.</summary>
    public Vector2 Direction { get; set; } = Vector2.UnitX;

    /// <summary>Travel speed in world units per second.</summary>
    public float Speed { get; set; } = 16.0f;

    /// <summary>Seconds before the projectile despawns on its own.</summary>
    public float Lifetime { get; set; } = 2.0f;

    /// <summary>Damage dealt to an enemy on impact.</summary>
    public int Damage { get; set; } = 1;

    private static readonly Vector4 SparkColor = new(1.0f, 0.95f, 0.55f, 1.0f);

    private float _age;

    public override void OnUpdate(float deltaTime)
    {
        _age += deltaTime;
        if (_age >= Lifetime)
        {
            Destroy();
            return;
        }

        TransformComponent transform = GetComponent<TransformComponent>();
        transform.Position += new Vector3(Direction * Speed * deltaTime, 0.0f);
        var here = new Vector2(transform.Position.X, transform.Position.Y);

        if (GameUtil.HitsObstacle(Scene, here))
        {
            Vfx.Burst(Scene, here, SparkColor, 5, 4.0f, 0.16f, 0.16f);
            Destroy();
            return;
        }

        foreach (Entity enemy in Scene.View<TransformComponent, Sprite2DComponent>())
        {
            if (!enemy.CompareTag("Enemy"))
            {
                continue;
            }

            EnemyController? ec = GameUtil.GetScript<EnemyController>(enemy);
            if (ec is null)
            {
                continue;
            }

            Vector3 pos = enemy.GetComponent<TransformComponent>().Position;
            float reach = ec.HitRadius;
            if (Vector2.DistanceSquared(here, new Vector2(pos.X, pos.Y)) <= reach * reach)
            {
                Vfx.Burst(Scene, here, SparkColor, 6, 5.0f, 0.2f, 0.2f);
                ec.TakeDamage(Damage);
                Destroy();
                return;
            }
        }
    }
}
