using System.Numerics;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>
/// A lift: a kinematic platform that rides between where it was placed and <see cref="Height"/> above, pausing at
/// each end. Being kinematic, it carries whatever stands on it.
/// </summary>
public sealed class Elevator : EntityBehaviour
{
    /// <summary>Gets or sets how high it rises, in meters.</summary>
    public float Height { get; set; } = 6.0f;

    /// <summary>Gets or sets its speed, in meters per second.</summary>
    public float Speed { get; set; } = 2.2f;

    /// <summary>Gets or sets the seconds it waits at each end.</summary>
    public float Wait { get; set; } = 2.5f;

    private TransformComponent _transform = null!;
    private Vector3 _bottom;
    private float _t;
    private float _direction = 1.0f;
    private float _waiting;

    public override void OnCreate()
    {
        _transform = GetComponent<TransformComponent>();
        _bottom = _transform.Position;
        _waiting = Wait;
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_waiting > 0.0f)
        {
            _waiting -= deltaTime;
            return;
        }

        _t = Math.Clamp(_t + _direction * deltaTime * Speed / Height, 0.0f, 1.0f);
        if (_t is <= 0.0f or >= 1.0f)
        {
            _direction = -_direction;
            _waiting = Wait;
        }

        float eased = _t * _t * (3.0f - 2.0f * _t);
        _transform.Position = _bottom + new Vector3(0.0f, Height * eased, 0.0f);
    }
}
