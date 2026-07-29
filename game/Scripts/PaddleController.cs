using System.Numerics;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Game.Scripts;

/// <summary>
/// Per-entity script that moves a paddle up and down with two keys. Attached to a paddle entity;
/// the engine runs it automatically each frame.
/// </summary>
internal sealed class PaddleController : EntityBehaviour
{
    private readonly Key _up;
    private readonly Key _down;
    private readonly float _speed;
    private readonly float _limit;

    public PaddleController(Key up, Key down, float speed, float limit)
    {
        _up = up;
        _down = down;
        _speed = speed;
        _limit = limit;
    }

    public override void OnUpdate(float deltaTime)
    {
        Transform transform = GetComponent<Transform>();
        Vector3 position = transform.Position;

        if (Input.GetKey(_up))
        {
            position.Y += _speed * deltaTime;
        }

        if (Input.GetKey(_down))
        {
            position.Y -= _speed * deltaTime;
        }

        position.Y = Math.Clamp(position.Y, -_limit, _limit);
        transform.Position = position;
    }
}
