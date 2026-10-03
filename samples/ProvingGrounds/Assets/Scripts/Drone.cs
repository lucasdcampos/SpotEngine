using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine.Scenes;
using Spot.Framework.Audio;

namespace ProvingGrounds;

/// <summary>
/// A hovering drone: a kinematic body flying a figure-eight around where it was placed, eye on the player, humming
/// from a looping positional voice. Hits make it jink; when it runs out of health its body turns dynamic, so it
/// drops and tumbles under real physics, trailing smoke, and blows up where it lands. It warps back in later.
/// Its parts are children in the scene: "Eye", "Rotor L", "Rotor R" and a "Lamp" light.
/// </summary>
public sealed class Drone : EntityBehaviour, IShootable, IResettable
{
    /// <summary>Gets or sets how many bullet hits it takes.</summary>
    public float Health { get; set; } = 3.0f;

    /// <summary>Gets or sets the half-size of its flight pattern, in meters (X, then Z).</summary>
    public Vector2 Patrol { get; set; } = new(6.0f, 3.0f);

    /// <summary>Gets or sets how fast it flies the pattern (radians per second).</summary>
    public float PatrolSpeed { get; set; } = 0.45f;

    /// <summary>Gets or sets the seconds before a downed drone warps back in.</summary>
    public float RespawnDelay { get; set; } = 6.0f;

    /// <summary>Gets or sets the points for downing it.</summary>
    public int Points { get; set; } = 150;

    private readonly List<Entity> _parts = new();
    private TransformComponent _transform = null!;
    private PhysicsBody3DComponent? _body;
    private SphereCollider3DComponent? _collider;
    private TransformComponent? _rotorLeft;
    private TransformComponent? _rotorRight;
    private Vector3 _home;
    private float _phase;
    private float _health;
    private Vector3 _dodge;
    private float _rotor;
    private float _fallTime;
    private float _smokeTimer;
    private State _state = State.Flying;
    private Voice _hum;
    private float _pitch;

    public Vector3 AimPoint => _transform.WorldPosition;

    public override void OnCreate()
    {
        _transform = GetComponent<TransformComponent>();
        Entity.TryGetComponent(out _body);
        Entity.TryGetComponent(out _collider);
        _home = _transform.Position;
        _phase = Random.Shared.NextSingle() * MathF.Tau;
        _health = Health;
        _pitch = 0.9f + Random.Shared.NextSingle() * 0.2f;

        foreach (Entity child in Entity.Children)
        {
            _parts.Add(child);
            if (child.Name == "Rotor L") _rotorLeft = child.GetComponent<TransformComponent>();
            if (child.Name == "Rotor R") _rotorRight = child.GetComponent<TransformComponent>();
        }

        StartHum();
    }

    public override void OnDestroy() => AudioManager.Stop(_hum);

    public override void OnUpdate(float deltaTime)
    {
        if (deltaTime <= 0.0f) return;

        switch (_state)
        {
            case State.Flying:
                Fly(deltaTime);
                break;
            case State.Falling:
                Fall(deltaTime);
                break;
        }
    }

    public HitResult OnShot(in ShotInfo shot)
    {
        if (_state != State.Flying || Playground.Current is not { } game) return HitResult.None;

        _health -= shot.Damage;
        Effects.Current?.Sparks(shot.Point, -shot.Direction + shot.Normal, 10, 7.0f, new Vector4(5.0f, 3.0f, 1.2f, 1.0f), 0.9f);

        // Jink away from the shot.
        Vector3 side = Vector3.Cross(shot.Direction, Vector3.UnitY);
        if (side.LengthSquared() > 1e-4f)
        {
            _dodge += Vector3.Normalize(side) * (Random.Shared.Next(2) == 0 ? -1.5f : 1.5f);
        }

        if (_health > 0.0f) return HitResult.Hit;

        Shoot(game, shot);
        return HitResult.Kill;
    }

    public void ResetState()
    {
        CancelInvoke();
        WarpIn();
    }

    private void Fly(float deltaTime)
    {
        _phase += deltaTime * PatrolSpeed;
        _dodge *= MathF.Exp(-2.5f * deltaTime);
        var path = new Vector3(MathF.Sin(_phase) * Patrol.X, MathF.Sin(_phase * 1.7f) * 0.5f, MathF.Sin(_phase * 2.0f) * Patrol.Y);
        _transform.Position = _home + path + _dodge;

        // Keep the eye on the player (the eye faces +Z, hence the half turn), banking with the motion.
        float yaw = _transform.Rotation.Y;
        if (Playground.Current is { Player.IsValid: true } game)
        {
            Vector3 to = game.Player.GetComponent<TransformComponent>().Position - _transform.Position;
            float target = MathF.Atan2(to.X, to.Z) * 180.0f / MathF.PI;
            yaw += DeltaAngle(yaw, target) * (1.0f - MathF.Exp(-4.0f * deltaTime));
        }

        float bank = MathF.Cos(_phase) * 10.0f;
        _transform.Rotation = new Vector3(MathF.Sin(_phase * 2.0f) * 4.0f, yaw, bank);

        _rotor += deltaTime * 1400.0f;
        if (_rotorLeft is not null) _rotorLeft.Rotation = new Vector3(0.0f, _rotor, 0.0f);
        if (_rotorRight is not null) _rotorRight.Rotation = new Vector3(0.0f, -_rotor, 0.0f);

        AudioManager.SetVoicePosition(_hum, _transform.Position);
    }

    // Shot down: hand the body to the physics simulation and let it fall.
    private void Shoot(Playground game, in ShotInfo shot)
    {
        _state = State.Falling;
        _fallTime = 0.0f;
        AudioManager.Stop(_hum);
        SetPart("Eye", false);
        SetPart("Lamp", false);

        if (_body is not null)
        {
            _body.IsKinematic = false;
            _body.IsDynamic = true;
            _body.Velocity = shot.Direction * 4.0f + new Vector3(0.0f, 2.0f, 0.0f);
            _body.AddImpulseAtPosition(shot.Direction * 3.0f, shot.Point + new Vector3(0.0f, 0.3f, 0.0f));
        }

        game.Stats.DronesDown++;
        game.Award(Points, "Drone down", _transform.Position + new Vector3(0.0f, 0.8f, 0.0f), HudColors.Danger);
        Sfx.PlayAt(Sfx.MetalHit, _transform.Position, 0.9f, 10.0f);
    }

    private void Fall(float deltaTime)
    {
        _fallTime += deltaTime;
        _smokeTimer -= deltaTime;
        if (_smokeTimer <= 0.0f)
        {
            _smokeTimer = 0.07f;
            Effects.Current?.Smoke(_transform.Position, 1);
            Effects.Current?.Sparks(_transform.Position, Vector3.UnitY, 2, 3.0f, new Vector4(4.0f, 2.0f, 0.6f, 1.0f), 1.0f);
        }

        if (_fallTime > 3.0f || _transform.Position.Y < -20.0f)
        {
            Burst();
        }
    }

    public override void OnCollisionEnter(Collision collision)
    {
        if (_state == State.Falling && _fallTime > 0.15f)
        {
            Burst();
        }
    }

    private void Burst()
    {
        if (_state != State.Falling) return;

        _state = State.Gone;
        Vector3 at = _transform.Position;
        foreach (Entity part in _parts) part.Enabled = false;
        if (_collider is not null) _collider.Enabled = false;
        if (_body is not null)
        {
            _body.IsDynamic = false;
            _body.IsKinematic = true;
            _body.Velocity = Vector3.Zero;
        }

        Explosions.Detonate(Scene, at, 3.2f, 1.5f, 30.0f);
        Invoke(WarpIn, RespawnDelay);
    }

    private void WarpIn()
    {
        _state = State.Flying;
        _health = Health;
        _dodge = Vector3.Zero;
        foreach (Entity part in _parts) part.Enabled = true;
        if (_collider is not null) _collider.Enabled = true;
        if (_body is not null)
        {
            _body.IsDynamic = false;
            _body.IsKinematic = true;
            _body.Velocity = Vector3.Zero;
        }

        _transform.Position = _home;
        _transform.Scale = new Vector3(0.05f);
        TweenScale(Vector3.One, 0.45f, Ease.OutBack);
        Effects.Current?.Pulse(_home, Vector3.UnitY, 2.2f, new Vector4(0.6f, 2.0f, 3.0f, 1.0f));
        Sfx.PlayAt(Sfx.JumpPad, _home, 0.5f, 6.0f);
        AudioManager.Stop(_hum);
        StartHum();
    }

    private void StartHum() =>
        _hum = AudioManager.Play(Sfx.DroneHum, 0.35f, _pitch, loop: true, spatial: true, position: _transform.Position, minDistance: 2.5f,
            maxDistance: 60.0f, bus: AudioMixer.SfxBus);

    private void SetPart(string name, bool enabled)
    {
        foreach (Entity part in _parts)
        {
            if (part.Name == name) part.Enabled = enabled;
        }
    }

    private static float DeltaAngle(float from, float to)
    {
        float delta = (to - from) % 360.0f;
        if (delta > 180.0f) delta -= 360.0f;
        if (delta < -180.0f) delta += 360.0f;
        return delta;
    }

    private enum State
    {
        Flying,
        Falling,
        Gone,
    }
}
