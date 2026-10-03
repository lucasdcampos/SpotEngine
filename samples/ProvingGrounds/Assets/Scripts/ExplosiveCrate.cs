using System.Numerics;
using Spot.Engine.Assets;
using Spot.Engine.Physics;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>
/// A red crate that blows up: a few bullets, or a nearby blast after a short fuse — so a cluster goes off as a chain.
/// The blast throws every physics body around, breaks the crate into tumbling shards (dynamic bodies spawned on the
/// debris layer, which the player walks through) and the crate rebuilds itself a while later. Its warning "Lamp" child
/// blinks faster as it takes damage.
/// </summary>
public sealed class ExplosiveCrate : Component, IShootable, IResettable
{
    private const int ShardCount = 7;

    private static Material? s_lampOn;
    private static Material? s_lampOff;
    private static Material? s_shard;

    /// <summary>Gets or sets how many bullet hits it takes.</summary>
    public float Health { get; set; } = 3.0f;

    /// <summary>Gets or sets the blast radius.</summary>
    public float Radius { get; set; } = 5.5f;

    /// <summary>Gets or sets the blast's push, in newton-seconds.</summary>
    public float Impulse { get; set; } = 140.0f;

    /// <summary>Gets or sets the seconds before a destroyed crate rebuilds.</summary>
    public float RespawnDelay { get; set; } = 14.0f;

    private readonly List<(Entity Shard, float Age)> _shards = new();
    private readonly List<Entity> _parts = new();
    private TransformComponent _transform = null!;
    private MeshComponent? _mesh;
    private BoxCollider3DComponent? _collider;
    private PhysicsBody3DComponent? _body;
    private MeshComponent? _lamp;
    private Vector3 _origin;
    private Vector3 _originRotation;
    private Vector3 _originScale;
    private float _health;
    private float _blink;
    private bool _armed = true;
    private bool _fuseLit;

    public Vector3 AimPoint => _transform.WorldPosition;

    public override void OnStart()
    {
        _transform = GetComponent<TransformComponent>();
        _origin = _transform.Position;
        _originRotation = _transform.Rotation;
        _originScale = _transform.Scale;
        Entity.TryGetComponent(out _mesh);
        Entity.TryGetComponent(out _collider);
        Entity.TryGetComponent(out _body);
        _health = Health;

        s_lampOn ??= new Material { Color = new Vector4(0.4f, 0.1f, 0.0f, 1.0f), EmissiveColor = new Vector3(1.0f, 0.35f, 0.05f), EmissiveIntensity = 6.0f };
        s_lampOff ??= new Material { Color = new Vector4(0.15f, 0.05f, 0.03f, 1.0f) };
        s_shard ??= new Material { Color = new Vector4(0.35f, 0.07f, 0.05f, 1.0f), Metallic = 0.3f };

        foreach (Entity child in Entity.Children)
        {
            _parts.Add(child);
            if (child.Name == "Lamp") child.TryGetComponent(out _lamp);
        }
    }

    public override void OnUpdate(float deltaTime)
    {
        // Blink: slow when whole, frantic when nearly gone.
        if (_armed && _lamp is not null)
        {
            float rate = _fuseLit ? 18.0f : 1.2f + 5.0f * (1.0f - _health / Health);
            _blink += deltaTime * rate;
            _lamp.Material = (_blink % 1.0f) < 0.5f ? s_lampOn : s_lampOff;
        }

        if (_armed && _transform.Position.Y < -30.0f)
        {
            ResetState();
        }

        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            (Entity shard, float age) = _shards[i];
            age += deltaTime;
            if (age > 6.0f || !shard.IsValid)
            {
                if (shard.IsValid) Destroy(shard);
                _shards.RemoveAt(i);
                continue;
            }

            _shards[i] = (shard, age);
            if (age > 5.0f)
            {
                shard.GetComponent<TransformComponent>().Scale *= MathF.Max(0.0f, 1.0f - deltaTime * 4.0f);
            }
        }
    }

    public HitResult OnShot(in ShotInfo shot)
    {
        if (!_armed) return HitResult.None;

        if (shot.Kind == DamageKind.Explosion)
        {
            if (!_fuseLit)
            {
                _fuseLit = true;
                Invoke(() => Detonate(chained: true), 0.12f + Random.Shared.NextSingle() * 0.2f);
            }

            return HitResult.Hit;
        }

        _health -= shot.Damage;
        Effects.Current?.Sparks(shot.Point, shot.Normal, 6, 5.0f, new Vector4(5.0f, 2.5f, 0.8f, 1.0f), 0.8f);
        if (_health > 0.0f) return HitResult.Hit;

        Detonate(chained: false);
        return HitResult.Kill;
    }

    public void ResetState()
    {
        CancelInvoke();
        Rebuild();
    }

    private void Detonate(bool chained)
    {
        if (!_armed) return;
        _armed = false;

        Vector3 at = _transform.Position;
        Vector3 velocity = _body?.Velocity ?? Vector3.Zero;
        SetVisible(false);

        if (Playground.Current is { } game)
        {
            game.Stats.CratesDestroyed++;
            if (chained)
            {
                game.Award(125, "Chain reaction", at + new Vector3(0.0f, 1.0f, 0.0f), HudColors.Warm);
            }
            else
            {
                game.Award(75, "Explosive crate", at + new Vector3(0.0f, 1.0f, 0.0f), HudColors.Warm);
            }
        }

        Explosions.Detonate(Scene, at, Radius, 3.0f, Impulse);
        SpawnShards(at, velocity);
        Invoke(Rebuild, RespawnDelay);
    }

    // Shards: small dynamic boxes on the debris layer, thrown out of the blast with a spin.
    private void SpawnShards(Vector3 at, Vector3 inherited)
    {
        float size = _originScale.X;
        for (int i = 0; i < ShardCount; i++)
        {
            Vector3 dir = Vector3.Normalize(new Vector3(Random.Shared.NextSingle() * 2.0f - 1.0f, 0.6f + Random.Shared.NextSingle(), Random.Shared.NextSingle() * 2.0f - 1.0f));
            Entity shard = Scene.Instantiate("Crate Shard");
            TransformComponent transform = shard.GetComponent<TransformComponent>();
            transform.Position = at + dir * size * 0.3f;
            transform.Rotation = new Vector3(Random.Shared.NextSingle() * 360.0f, Random.Shared.NextSingle() * 360.0f, 0.0f);
            transform.Scale = new Vector3(0.12f + 0.2f * Random.Shared.NextSingle(), 0.05f + 0.08f * Random.Shared.NextSingle(), 0.12f + 0.25f * Random.Shared.NextSingle()) * size;
            shard.AddComponent(new MeshComponent { ModelPath = "builtin:Mesh/Cube", Material = s_shard });
            shard.AddComponent(new BoxCollider3DComponent { Layer = Layers.Debris });
            var body = shard.AddComponent(new PhysicsBody3DComponent { Mass = 0.6f, Friction = 0.7f, Velocity = inherited + dir * (6.0f + 8.0f * Random.Shared.NextSingle()) });
            body.AddImpulseAtPosition(dir * 0.4f, transform.Position + new Vector3(0.05f, 0.05f, 0.0f));
            _shards.Add((shard, 0.0f));
        }
    }

    private void Rebuild()
    {
        _armed = true;
        _fuseLit = false;
        _health = Health;
        _transform.Position = _origin;
        _transform.Rotation = _originRotation;
        if (_body is not null) _body.Velocity = Vector3.Zero;
        SetVisible(true);
        _transform.Scale = _originScale * 0.1f;
        TweenScale(_originScale, 0.35f, Ease.OutBack);
        Effects.Current?.Pulse(_origin + new Vector3(0.0f, 0.05f, 0.0f), Vector3.UnitY, 1.6f, new Vector4(3.0f, 1.2f, 0.4f, 1.0f));
    }

    private void SetVisible(bool visible)
    {
        if (_mesh is not null) _mesh.Enabled = visible;
        if (_collider is not null) _collider.Enabled = visible;
        if (_body is not null) _body.Enabled = visible;
        foreach (Entity part in _parts) part.Enabled = visible;
    }
}
