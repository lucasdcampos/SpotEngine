using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>
/// A crate, a ball, a domino: a dynamic body that thuds when it hits something hard (a collision callback), goes back
/// where it started when the playground is reset — setting a dynamic body's transform teleports it — and comes back
/// by itself if it falls off the world. Its <see cref="Surface"/> picks the impact effect bullets make on it.
/// </summary>
public sealed class PhysicsProp : Component, IResettable
{
    private static float s_windowStart;
    private static int s_soundsInWindow;

    /// <summary>Gets or sets what the prop is made of.</summary>
    public Surface Surface { get; set; } = Surface.Wood;

    /// <summary>Gets or sets how loud its impacts are.</summary>
    public float ImpactVolume { get; set; } = 1.0f;

    private TransformComponent _transform = null!;
    private PhysicsBody3DComponent? _body;
    private Vector3 _position;
    private Vector3 _rotation;
    private float _lastSound;

    public override void OnStart()
    {
        _transform = GetComponent<TransformComponent>();
        Entity.TryGetComponent(out _body);
        _position = _transform.Position;
        _rotation = _transform.Rotation;
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_transform.Position.Y < -30.0f)
        {
            ResetState();
        }
    }

    public override void OnCollisionEnter(Collision collision)
    {
        if (_body is null) return;

        float speed = _body.Velocity.Length();
        float now = Spot.Engine.Time.UnscaledTime;
        if (speed < 2.5f || now - _lastSound < 0.15f) return;

        // A collapsing stack starts dozens of contacts at once: let only a handful be heard.
        if (now - s_windowStart > 0.1f)
        {
            s_windowStart = now;
            s_soundsInWindow = 0;
        }

        if (++s_soundsInWindow > 5) return;

        _lastSound = now;
        float volume = Math.Clamp((speed - 2.5f) / 8.0f, 0.1f, 1.0f) * 0.6f * ImpactVolume;
        Sfx.PlayAt(Surface == Surface.Metal ? Sfx.MetalHit : Sfx.Thud, collision.Point, volume, 4.0f, 0.15f);
    }

    public void ResetState()
    {
        _transform.Position = _position;
        _transform.Rotation = _rotation;
        if (_body is not null) _body.Velocity = Vector3.Zero;
    }
}
