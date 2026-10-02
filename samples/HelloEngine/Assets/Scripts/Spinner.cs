using System.Numerics;
using Spot.Engine.Scenes;

namespace HelloEngine;

/// <summary>
/// Spins its entity and bobs it up and down. The entity is a kinematic physics body, so as it moves it shoves the
/// falling cubes around.
/// </summary>
public sealed class Spinner : EntityBehaviour
{
    /// <summary>Gets or sets the spin speed, in degrees per second.</summary>
    public float DegreesPerSecond { get; set; } = 40.0f;

    /// <summary>Gets or sets how far the entity bobs above and below where it started.</summary>
    public float BobHeight { get; set; } = 0.5f;

    private TransformComponent _transform = null!;
    private Vector3 _origin;
    private float _time;

    public override void OnCreate()
    {
        _transform = GetComponent<TransformComponent>();
        _origin = _transform.Position;
    }

    public override void OnUpdate(float deltaTime)
    {
        _time += deltaTime;
        Vector3 rotation = _transform.Rotation;
        _transform.Rotation = rotation with { Y = (rotation.Y + DegreesPerSecond * deltaTime) % 360.0f };
        _transform.Position = _origin + new Vector3(0, BobHeight * MathF.Sin(_time * 1.5f), 0);
    }
}
