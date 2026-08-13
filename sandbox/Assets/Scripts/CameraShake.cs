using System;
using System.Numerics;
using Spot.Scenes;

namespace Spot.Game;

/// <summary>
/// Traumatic screen shake for the camera it is attached to. Gameplay code adds "trauma" via the static
/// <see cref="Add"/> (on an enemy death, a hit, etc.); the shake magnitude is trauma², so it kicks hard
/// and eases out smoothly. It offsets and rolls the camera around the position it started at, then
/// decays back to rest. Only one camera shakes at a time (the last one created registers itself).
/// </summary>
public sealed class CameraShake : EntityBehaviour
{
    /// <summary>Maximum positional offset in world units at full trauma.</summary>
    public float MaxOffset { get; set; } = 0.7f;

    /// <summary>Maximum roll in degrees at full trauma.</summary>
    public float MaxRoll { get; set; } = 2.5f;

    /// <summary>How much trauma decays per second.</summary>
    public float DecayPerSecond { get; set; } = 1.8f;

    private static CameraShake? s_active;

    private readonly Random _rng = new();
    private Vector3 _basePosition;
    private float _trauma;
    private bool _wasResting = true;

    /// <summary>Adds trauma (0..1) to the active camera shake, if any.</summary>
    public static void Add(float amount) => s_active?.AddTrauma(amount);

    private void AddTrauma(float amount) => _trauma = Math.Clamp(_trauma + amount, 0.0f, 1.0f);

    public override void OnCreate()
    {
        _basePosition = GetComponent<TransformComponent>().Position;
        s_active = this;
    }

    public override void OnUpdate(float deltaTime)
    {
        TransformComponent transform = GetComponent<TransformComponent>();

        if (_trauma <= 0.0f)
        {
            // Snap back to rest exactly once, then leave the transform alone.
            if (!_wasResting)
            {
                transform.Position = _basePosition;
                transform.Rotation = new Vector3(transform.Rotation.X, transform.Rotation.Y, 0.0f);
                _wasResting = true;
            }

            return;
        }

        _wasResting = false;
        float shake = _trauma * _trauma;
        float offX = (float)(_rng.NextDouble() * 2.0 - 1.0) * MaxOffset * shake;
        float offY = (float)(_rng.NextDouble() * 2.0 - 1.0) * MaxOffset * shake;
        float roll = (float)(_rng.NextDouble() * 2.0 - 1.0) * MaxRoll * shake;

        transform.Position = _basePosition + new Vector3(offX, offY, 0.0f);
        transform.Rotation = new Vector3(transform.Rotation.X, transform.Rotation.Y, roll);

        _trauma = Math.Max(0.0f, _trauma - DecayPerSecond * deltaTime);
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(s_active, this))
        {
            s_active = null;
        }
    }
}
