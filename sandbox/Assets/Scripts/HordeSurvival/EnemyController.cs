using System;
using System.Numerics;
using Spot.Scenes;

namespace Spot.Game;

/// <summary>The visual/behaviour archetype of an enemy. Each reads as a distinct geometric silhouette.</summary>
public enum EnemyKind
{
    /// <summary>Small red square, average speed, dies in one hit.</summary>
    Grunt,

    /// <summary>Magenta bar (elongated quad) that points at the player and charges fast.</summary>
    Dasher,

    /// <summary>Orange spinning diamond; a bit tougher.</summary>
    Spinner,

    /// <summary>Big slow purple rectangle that soaks several hits and hurts on contact.</summary>
    Brute,
}

/// <summary>
/// One enemy. It homes in on the player, drains the player's health on contact, and takes damage from
/// projectiles until its health runs out, then dies in a burst of particles with a jolt of screen shake.
/// Enemies come in several <see cref="EnemyKind"/>s built from rotated/scaled opaque quads, so the field
/// reads as varied geometry. Create one with the static <see cref="Spawn"/>.
/// </summary>
public sealed class EnemyController : EntityBehaviour
{
    public EnemyKind Kind { get; set; }

    /// <summary>Movement speed in world units per second.</summary>
    public float Speed { get; set; } = 2.3f;

    /// <summary>Remaining hit points.</summary>
    public int Hp { get; set; } = 1;

    /// <summary>Health drained from the player per second while in contact.</summary>
    public float ContactDamagePerSecond { get; set; } = 20.0f;

    /// <summary>Distance at which this enemy touches the player.</summary>
    public float ContactRadius { get; set; } = 0.8f;

    /// <summary>Distance within which a projectile counts as hitting this enemy.</summary>
    public float HitRadius { get; set; } = 0.6f;

    /// <summary>Score awarded when this enemy dies.</summary>
    public int Points { get; set; } = 1;

    /// <summary>Continuous spin in degrees per second (Spinner only).</summary>
    public float SpinSpeed { get; set; }

    /// <summary>Whether the sprite rotates to point its long axis at the player (Dasher).</summary>
    public bool FacePlayer { get; set; }

    private Vector4 _baseColor = Vector4.One;
    private float _flash;
    private bool _dead;

    /// <summary>Creates and configures an enemy of the given kind at a world position, scaled for the wave.</summary>
    public static Entity Spawn(Scene scene, Vector2 position, EnemyKind kind, int wave)
    {
        (Vector4 color, Vector2 size, float baseSpeed, int hp, float dmg, int points, float spin, bool face) = StatsFor(kind);

        // Ramp speed with the wave, but keep every kind kiteable (the player moves at 6.5).
        float speed = MathF.Min(baseSpeed + wave * 0.12f, baseSpeed + 1.8f);
        float radius = MathF.Max(size.X, size.Y) * 0.5f;

        Entity enemy = scene.Instantiate("Enemy");
        enemy.Tag = "Enemy";
        enemy.AddComponent(new Sprite2DComponent
        {
            Color = color,
            Texture = kind switch
            {
                EnemyKind.Dasher => Shapes.Diamond,
                EnemyKind.Spinner => Shapes.Hexagon,
                EnemyKind.Brute => Shapes.Pentagon,
                _ => Shapes.Circle,
            },
        });

        TransformComponent transform = enemy.GetComponent<TransformComponent>();
        transform.Position = new Vector3(position, 0.0f);
        transform.Scale = new Vector3(size.X, size.Y, 1.0f);

        EnemyController ec = enemy.AddScript<EnemyController>();
        ec.Kind = kind;
        ec.Speed = speed;
        ec.Hp = hp;
        ec.ContactDamagePerSecond = dmg;
        ec.Points = points;
        ec.SpinSpeed = spin;
        ec.FacePlayer = face;
        ec.ContactRadius = radius + 0.45f;
        ec.HitRadius = radius + 0.25f;

        // A little spawn-in poof so enemies pop into being rather than blink in.
        Vfx.Burst(scene, position, color, 8, 3.0f, MathF.Max(size.X, size.Y) * 0.6f, 0.35f);
        return enemy;
    }

    private static (Vector4 Color, Vector2 Size, float Speed, int Hp, float Dmg, int Points, float Spin, bool Face) StatsFor(EnemyKind kind) => kind switch
    {
        EnemyKind.Dasher => (new Vector4(0.95f, 0.32f, 0.78f, 1.0f), new Vector2(0.85f, 0.34f), 3.8f, 1, 26.0f, 2, 0.0f, true),
        EnemyKind.Spinner => (new Vector4(1.0f, 0.62f, 0.15f, 1.0f), new Vector2(0.62f, 0.62f), 2.7f, 2, 24.0f, 3, 220.0f, false),
        EnemyKind.Brute => (new Vector4(0.62f, 0.32f, 0.86f, 1.0f), new Vector2(1.35f, 1.05f), 1.35f, 5, 42.0f, 6, 0.0f, false),
        _ => (new Vector4(0.92f, 0.30f, 0.26f, 1.0f), new Vector2(0.6f, 0.6f), 2.3f, 1, 20.0f, 1, 0.0f, false),
    };

    public override void OnCreate() => _baseColor = GetComponent<Sprite2DComponent>().Color;

    public override void OnUpdate(float deltaTime)
    {
        if (HordeGameState.GameOver)
        {
            return;
        }

        TransformComponent transform = GetComponent<TransformComponent>();
        Entity? player = Find("Player");
        if (player is not null)
        {
            Vector3 playerPos = player.Value.GetComponent<TransformComponent>().Position;
            Vector3 toPlayer = playerPos - transform.Position;
            toPlayer.Z = 0.0f;

            float dist = toPlayer.Length();
            if (dist > 0.0001f)
            {
                Vector3 dir = toPlayer / dist;
                transform.Position += dir * Speed * deltaTime;

                if (FacePlayer)
                {
                    float angle = MathF.Atan2(dir.Y, dir.X) * (180.0f / MathF.PI);
                    transform.Rotation = new Vector3(0.0f, 0.0f, angle);
                }
            }

            if (dist <= ContactRadius)
            {
                HordeGameState.PlayerHealth -= ContactDamagePerSecond * deltaTime;
            }
        }

        if (SpinSpeed != 0.0f)
        {
            transform.Rotation += new Vector3(0.0f, 0.0f, SpinSpeed * deltaTime);
        }

        // Fade the white hit-flash back to the base color.
        if (_flash > 0.0f)
        {
            _flash = MathF.Max(0.0f, _flash - deltaTime * 6.0f);
            GetComponent<Sprite2DComponent>().Color = Vector4.Lerp(_baseColor, Vector4.One, _flash);
        }
    }

    /// <summary>Applies projectile damage; flashes and, at zero health, dies. Ignored once already dead
    /// (destruction is deferred, so several bullets can reach the same enemy in one frame).</summary>
    public void TakeDamage(int amount)
    {
        if (_dead)
        {
            return;
        }

        Hp -= amount;
        _flash = 1.0f;

        Vector2 pos = Position2D();
        Vfx.Burst(Scene, pos, new Vector4(1.0f, 1.0f, 1.0f, 1.0f), 5, 4.5f, 0.18f, 0.18f);

        // A floating world-space damage number rising off the hit — the demo's showcase of the engine's
        // world text.
        DamageNumber.Spawn(Scene, pos + new Vector2(0.0f, 0.4f), amount, new Vector4(1.0f, 0.86f, 0.3f, 1.0f));

        if (Hp <= 0)
        {
            Die(pos);
        }
    }

    private void Die(Vector2 pos)
    {
        _dead = true;

        (int count, float shake, float size) = Kind switch
        {
            EnemyKind.Brute => (26, 0.5f, 0.5f),
            EnemyKind.Spinner => (16, 0.24f, 0.32f),
            _ => (12, 0.18f, 0.3f),
        };

        Vfx.Burst(Scene, pos, _baseColor, count, 6.5f, size, 0.5f);
        HordeGameState.Score += Points;
        CameraShake.Add(shake);
        Destroy();
    }

    private Vector2 Position2D()
    {
        Vector3 p = GetComponent<TransformComponent>().Position;
        return new Vector2(p.X, p.Y);
    }
}
