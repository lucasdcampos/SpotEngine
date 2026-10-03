using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>
/// A launcher grenade, spawned with a mesh, a sphere collider and a dynamic body: it arcs under gravity and goes off on
/// the first thing it touches (a collision callback) or when its fuse runs out. A ray swept between frames catches
/// anything thin it would otherwise fly through.
/// </summary>
public sealed class Grenade : EntityBehaviour
{
    /// <summary>Gets or sets the damage at the blast's center.</summary>
    public float Damage { get; set; } = 3.0f;

    /// <summary>Gets or sets how far the blast reaches.</summary>
    public float Radius { get; set; } = 4.8f;

    /// <summary>Gets or sets the blast's push, in newton-seconds.</summary>
    public float Impulse { get; set; } = 70.0f;

    /// <summary>Gets or sets the seconds before it goes off on its own.</summary>
    public float Fuse { get; set; } = 4.0f;

    private TransformComponent _transform = null!;
    private Vector3 _last;
    private float _age;
    private bool _exploded;

    public override void OnCreate()
    {
        _transform = GetComponent<TransformComponent>();
        _last = _transform.Position;
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_exploded) return;

        _age += deltaTime;
        Vector3 position = _transform.Position;
        Vector3 step = position - _last;

        uint solid = Layers.Shots & ~(1u << Layers.Debris);
        if (step.LengthSquared() > 1e-6f && Scene.Raycast(_last, step, step.Length(), out RaycastHit hit, solid))
        {
            Explode(hit.Point + hit.Normal * 0.15f);
            return;
        }

        _last = position;
        Effects.Current?.Sparks(position, -step, 1, 1.5f, new Vector4(4.0f, 1.8f, 0.5f, 1.0f), 0.4f, 0.0f);

        if (_age >= Fuse)
        {
            Explode(position);
        }
    }

    public override void OnCollisionEnter(Collision collision) => Explode(_transform.Position);

    private void Explode(Vector3 at)
    {
        if (_exploded) return;
        _exploded = true;

        HitResult result = Explosions.Detonate(Scene, at, Radius, Damage, Impulse);
        if (result != HitResult.None && Playground.Current is { } game)
        {
            game.Stats.ShotsHit++;
        }

        Destroy();
    }
}
