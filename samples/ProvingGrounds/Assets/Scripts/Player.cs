using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>
/// What the engine's <see cref="CharacterController3D"/> leaves to the game: footsteps and landings you can
/// hear and feel, camera shake from explosions, a slight roll into strafes, and launches from jump pads. Movement
/// itself — Quake-style acceleration, air strafing, crouching, jumping, mouse look — is the component's.
/// </summary>
public sealed class Player : Component
{
    /// <summary>Gets or sets the distance between footsteps at a run, in meters.</summary>
    public float StrideLength { get; set; } = 2.3f;

    /// <summary>Gets or sets how far the view rolls into a strafe, in degrees.</summary>
    public float StrafeRoll { get; set; } = 1.4f;

    /// <summary>Gets or sets the largest camera shake, in degrees.</summary>
    public float MaxShake { get; set; } = 4.0f;

    /// <summary>Gets the player of the playing scene.</summary>
    public static Player? Current { get; private set; }

    /// <summary>Gets the horizontal speed, in meters per second.</summary>
    public float Speed { get; private set; }

    public bool Grounded => _controller?.IsGrounded ?? true;

    public bool Crouching => _controller?.IsCrouching ?? false;

    public bool Flying => _controller?.IsNoClip ?? false;

    private CharacterController3D? _controller;
    private PhysicsBody3D? _body;
    private Transform _transform = null!;
    private Transform? _camera;
    private bool _wasGrounded = true;
    private Vector3 _launch;
    private float _launchHold;
    private float _fallSpeed;
    private float _stride;
    private bool _leftFoot;
    private float _roll;
    private float _dip;
    private float _time;

    public override void OnStart()
    {
        Current = this;
        _transform = GetComponent<Transform>();
        Entity.TryGetComponent(out _controller);
        Entity.TryGetComponent(out _body);
        foreach (Entity child in Entity.Children)
        {
            if (child.HasComponent<Camera>())
            {
                _camera = child.GetComponent<Transform>();
                break;
            }
        }
    }

    public override void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    /// <summary>
    /// Throws the player, replacing their velocity (jump pads, blasts). The controller brakes a grounded player with
    /// friction, so the horizontal part is held until they are off the ground.
    /// </summary>
    public void Launch(Vector3 velocity)
    {
        if (_body is null) return;
        _body.Velocity = velocity;
        _launch = velocity;
        _launchHold = 0.25f;
        _fallSpeed = 0.0f;
    }

    // Runs after the character controller has applied this frame's ground friction, and before the physics step.
    public override void OnFixedUpdate(float deltaTime)
    {
        if (_launchHold <= 0.0f || _body is null) return;

        _launchHold -= deltaTime;
        if (_controller is { IsGrounded: false })
        {
            _launchHold = 0.0f; // airborne: the launch carries itself
            return;
        }

        _body.Velocity = new Vector3(_launch.X, MathF.Max(_body.Velocity.Y, _launch.Y), _launch.Z);
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_body is null || _controller is null || deltaTime <= 0.0f) return;

        _time += deltaTime;
        Speed = new Vector2(_body.Velocity.X, _body.Velocity.Z).Length();
        bool grounded = _controller.IsGrounded && !_controller.IsNoClip;

        if (!grounded)
        {
            _fallSpeed = MathF.Min(_fallSpeed, _body.Velocity.Y);
        }
        else if (!_wasGrounded)
        {
            Landed(-_fallSpeed);
            _fallSpeed = 0.0f;
        }

        _wasGrounded = grounded;

        if (grounded && Speed > 1.5f)
        {
            _stride += Speed * deltaTime;
            float length = StrideLength * (Speed < 5.0f ? 0.75f : 1.0f);
            if (_stride >= length)
            {
                _stride -= length;
                _leftFoot = !_leftFoot;
                float volume = (_controller.IsCrouching ? 0.15f : 0.32f) * MathF.Min(1.0f, Speed / 6.0f);
                Sfx.Play(Sfx.Footstep, volume, 0.08f, _leftFoot ? 0.94f : 1.04f);
            }
        }
        else if (grounded)
        {
            _stride = StrideLength * 0.6f; // the first step after standing still lands quickly
        }
    }

    // After the controller has posed the camera: roll into strafes, dip on landings, shake from blasts.
    public override void OnLateUpdate(float deltaTime)
    {
        if (_camera is null || _controller is null || _body is null) return;

        Matrix4x4 yaw = Matrix4x4.CreateRotationY(_controller.Yaw * MathF.PI / 180.0f);
        Vector3 right = Vector3.TransformNormal(Vector3.UnitX, yaw);
        float lateral = Vector3.Dot(_body.Velocity, right) / MathF.Max(1.0f, _controller.RunSpeed);
        float targetRoll = _controller.IsGrounded ? -Math.Clamp(lateral, -1.0f, 1.0f) * StrafeRoll : 0.0f;
        _roll += (targetRoll - _roll) * (1.0f - MathF.Exp(-8.0f * deltaTime));
        _dip = MathF.Max(0.0f, _dip - deltaTime * 3.0f);

        float trauma = Playground.Current?.Trauma ?? 0.0f;
        float shake = trauma * trauma * MaxShake;
        float t = _time * 23.0f;
        var jitter = new Vector3(
            MathF.Sin(t * 1.1f) + MathF.Sin(t * 2.7f + 1.3f) * 0.5f,
            MathF.Sin(t * 1.3f + 2.1f) + MathF.Sin(t * 3.1f) * 0.5f,
            MathF.Sin(t * 0.9f + 4.2f) + MathF.Sin(t * 2.3f + 0.7f) * 0.5f) * (shake / 1.5f);

        float dip = _dip * _dip;
        _camera.Rotation = new Vector3(_controller.Pitch - 3.5f * dip + jitter.X, jitter.Y, _roll + jitter.Z * 1.4f);
        _camera.Position -= new Vector3(0.0f, 0.12f * dip, 0.0f);
    }

    private void Landed(float speed)
    {
        if (speed < 4.0f) return;

        float strength = Math.Clamp((speed - 4.0f) / 10.0f, 0.0f, 1.0f);
        _dip = MathF.Max(_dip, 0.4f + 0.6f * strength);
        Sfx.Play(Sfx.Land, 0.3f + 0.5f * strength, 0.06f);
        WeaponController.Current?.Land(0.3f + 0.7f * strength);
        if (strength > 0.25f)
        {
            Effects.Current?.Dust(_transform.Position + new Vector3(0.0f, 0.05f, 0.0f), Vector3.UnitY, (int)(4 + 10 * strength));
        }
    }
}
