using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Framework;

namespace HelloEngine;

/// <summary>
/// Orbits the camera around a point: slowly on its own, or by dragging with the right mouse button; the scroll
/// wheel zooms.
/// </summary>
public sealed class CameraOrbit : Component
{
    /// <summary>Gets or sets the point the camera looks at.</summary>
    public Vector3 Target { get; set; } = new(0, 1, 0);

    /// <summary>Gets or sets the distance from the target.</summary>
    public float Distance { get; set; } = 15.0f;

    /// <summary>Gets or sets the angle above the horizon, in degrees.</summary>
    public float Pitch { get; set; } = 25.0f;

    /// <summary>Gets or sets the idle orbit speed, in degrees per second.</summary>
    public float IdleSpeed { get; set; } = 6.0f;

    private TransformComponent _transform = null!;
    private Vector2 _lastMouse;
    private float _yaw;

    public override void OnStart()
    {
        _transform = GetComponent<TransformComponent>();
        _lastMouse = Input.MousePosition;
    }

    public override void OnUpdate(float deltaTime)
    {
        Vector2 mouse = Input.MousePosition;
        if (Input.GetMouseButton(MouseButton.Right))
        {
            _yaw -= (mouse.X - _lastMouse.X) * 0.25f;
            Pitch = Math.Clamp(Pitch + (mouse.Y - _lastMouse.Y) * 0.25f, 5.0f, 80.0f);
        }
        else
        {
            _yaw += IdleSpeed * deltaTime;
        }

        _lastMouse = mouse;
        Distance = Math.Clamp(Distance - Input.MouseScrollDelta.Y, 6.0f, 30.0f);

        float yaw = _yaw * (MathF.PI / 180.0f);
        float pitch = Pitch * (MathF.PI / 180.0f);
        var offset = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        _transform.Position = Target + Distance * offset;
        _transform.Rotation = new Vector3(-Pitch, _yaw, 0);
    }
}
