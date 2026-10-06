using System.Numerics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>Spins its entity around Y and floats it up and down — signs, beacons, pickups.</summary>
public sealed class Hover : Component
{
    /// <summary>Gets or sets the spin, in degrees per second.</summary>
    public float Spin { get; set; } = 30.0f;

    /// <summary>Gets or sets how far it floats up and down.</summary>
    public float Bob { get; set; } = 0.15f;

    /// <summary>Gets or sets the float frequency, in cycles per second.</summary>
    public float Frequency { get; set; } = 0.4f;

    private Transform _transform = null!;
    private Vector3 _origin;
    private float _time;

    public override void OnStart()
    {
        _transform = GetComponent<Transform>();
        _origin = _transform.Position;
        _time = Random.Shared.NextSingle() * 10.0f;
    }

    public override void OnUpdate(float deltaTime)
    {
        _time += deltaTime;
        _transform.Position = _origin + new Vector3(0.0f, MathF.Sin(_time * Frequency * MathF.Tau) * Bob, 0.0f);
        Vector3 rotation = _transform.Rotation;
        _transform.Rotation = rotation with { Y = (rotation.Y + Spin * deltaTime) % 360.0f };
    }
}
