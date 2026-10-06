using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Engine;

namespace SolarSystem;

/// <summary>
/// An orbit camera with smooth motion: drag with either mouse button (or use the arrow keys / WASD) to orbit, the
/// scroll wheel (or Q/E) to zoom. <see cref="Focus"/> glides to a body and keeps it centered as it orbits;
/// <see cref="Overview"/> pulls back to the whole system.
/// </summary>
/// <remarks>
/// Runs in <see cref="OnLateUpdate"/>, after <see cref="Simulation"/> has moved the bodies this frame, so a body
/// being followed never lags a frame behind.
/// </remarks>
public sealed class OrbitCamera : Component
{
    private const float DragThreshold = 4.0f;

    /// <summary>Gets or sets the distance of the overview shot.</summary>
    [InspectorRange(5.0f, 500.0f, 0.5f)]
    public float OverviewDistance { get; set; } = 92.0f;

    /// <summary>Gets or sets the angle above the orbital plane of the overview shot, in degrees.</summary>
    [InspectorRange(-89.0f, 89.0f, 0.5f)]
    public float OverviewPitch { get; set; } = 24.0f;

    [InspectorRange(5.0f, 1000.0f, 1.0f)]
    public float MaxDistance { get; set; } = 320.0f;

    /// <summary>Gets or sets the orbit speed per pixel dragged, in degrees.</summary>
    [InspectorRange(0.01f, 2.0f, 0.01f)]
    public float Sensitivity { get; set; } = 0.25f;

    /// <summary>Gets or sets how fast the camera drifts around on its own once left alone, in degrees per second.</summary>
    [InspectorRange(0.0f, 30.0f, 0.1f)]
    public float IdleDrift { get; set; } = 1.2f;

    /// <summary>Gets or sets how quickly the camera catches up with its target; higher is snappier.</summary>
    [InspectorRange(0.5f, 30.0f, 0.1f)]
    public float Smoothing { get; set; } = 6.0f;

    private Transform _transform = null!;
    private Camera? _camera;

    private float _yaw = -35.0f;
    private float _pitch = 55.0f;
    private float _distance;
    private float _targetYaw;
    private float _targetPitch;
    private float _targetDistance;

    // The pivot glides from where it was to the focused body over the transition, then locks onto the body.
    private Vector3 _pivot;
    private Vector3 _transitionStart;
    private float _transition = 1.0f;

    private Vector2 _lastMouse;
    private Vector2 _pressPosition;
    private bool _pressed;
    private float _idleTime;

    /// <summary>Gets the body the camera follows, or <see langword="null"/> in the overview.</summary>
    public CelestialBody? Focused { get; private set; }

    /// <summary>Gets whether the mouse is dragging the view (so a release is not a click).</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Gets or sets whether the camera ignores the mouse — while the pointer is over the HUD, say.</summary>
    [HideInInspector]
    public bool MouseBlocked { get; set; }

    /// <summary>Gets or sets an extra yaw speed, in degrees per second, for slow cinematic turns (the tour).</summary>
    [HideInInspector]
    public float AutoOrbit { get; set; }

    public override void OnStart()
    {
        _transform = GetComponent<Transform>();
        Entity.TryGetComponent(out _camera);
        _lastMouse = Input.MousePosition;

        // Start high and far, then settle into the overview: a slow opening shot.
        _targetYaw = _yaw + 20.0f;
        _targetPitch = OverviewPitch;
        _targetDistance = OverviewDistance;
        _distance = OverviewDistance * 2.4f;
        Apply();
    }

    /// <summary>Glides to <paramref name="body"/> and follows it.</summary>
    public void Focus(CelestialBody body)
    {
        if (Focused != body)
        {
            _transitionStart = _pivot;
            _transition = 0.0f;
        }

        Focused = body;
        _targetDistance = FocusDistance(body);
        _idleTime = 0.0f;
        if (body.RingOuter > 0.0f)
        {
            FrameRings(body.PoleAxis);
        }
    }

    /// <summary>Returns to the view of the whole system.</summary>
    public void Overview()
    {
        if (Focused is not null)
        {
            _transitionStart = _pivot;
            _transition = 0.0f;
        }

        Focused = null;
        _targetDistance = OverviewDistance;
        _targetPitch = OverviewPitch;
    }

    public override void OnLateUpdate(float deltaTime)
    {
        bool active = HandleMouse();
        active |= HandleKeys(deltaTime);
        _idleTime = active ? 0.0f : _idleTime + deltaTime;

        // Left alone, the view drifts slowly so the scene never looks frozen.
        float drift = AutoOrbit + (_idleTime > 4.0f ? IdleDrift * MathF.Min(1.0f, (_idleTime - 4.0f) * 0.5f) : 0.0f);
        _targetYaw += drift * deltaTime;

        float minDistance = Focused is not null ? Focused.VisualRadius * 1.6f : 6.0f;
        _targetDistance = Math.Clamp(_targetDistance, minDistance, MaxDistance);
        _targetPitch = Math.Clamp(_targetPitch, -85.0f, 85.0f);

        float k = 1.0f - MathF.Exp(-Smoothing * deltaTime);
        _yaw += (_targetYaw - _yaw) * k;
        _pitch += (_targetPitch - _pitch) * k;

        // Zoom eases in log space, so it feels the same at a planet as across the whole system.
        float logDistance = MathF.Log(MathF.Max(_distance, 0.01f));
        _distance = MathF.Exp(logDistance + (MathF.Log(_targetDistance) - logDistance) * k);

        Vector3 goal = Focused?.Position ?? Vector3.Zero;
        if (_transition < 1.0f)
        {
            _transition = MathF.Min(1.0f, _transition + deltaTime / 1.4f);
            float t = _transition * _transition * (3.0f - 2.0f * _transition);
            _pivot = Vector3.Lerp(_transitionStart, goal, t);
        }
        else
        {
            _pivot = goal;
        }

        Apply();
    }

    private bool HandleMouse()
    {
        Vector2 mouse = Input.MousePosition;
        Vector2 delta = mouse - _lastMouse;
        _lastMouse = mouse;
        bool active = false;

        bool down = Input.GetMouseButton(MouseButton.Left) || Input.GetMouseButton(MouseButton.Right);
        if (!_pressed && down && !MouseBlocked
            && (Input.GetMouseButtonDown(MouseButton.Left) || Input.GetMouseButtonDown(MouseButton.Right)))
        {
            _pressed = true;
            _pressPosition = mouse;
        }

        if (_pressed && !down)
        {
            _pressed = false;
            IsDragging = false;
        }

        if (_pressed)
        {
            if (!IsDragging && Vector2.Distance(mouse, _pressPosition) > DragThreshold)
            {
                IsDragging = true;
            }

            if (IsDragging)
            {
                _targetYaw -= delta.X * Sensitivity;
                _targetPitch += delta.Y * Sensitivity;
                active = true;
            }
        }

        float scroll = MouseBlocked ? 0.0f : Input.MouseScrollDelta.Y;
        if (scroll != 0.0f)
        {
            _targetDistance *= MathF.Pow(0.87f, scroll);
            active = true;
        }

        return active;
    }

    private bool HandleKeys(float deltaTime)
    {
        float yaw = Axis(Key.Left, Key.A) - Axis(Key.Right, Key.D);
        float pitch = Axis(Key.Up, Key.W) - Axis(Key.Down, Key.S);
        float zoom = Axis(Key.E, Key.E) - Axis(Key.Q, Key.Q);
        if (yaw == 0.0f && pitch == 0.0f && zoom == 0.0f)
        {
            return false;
        }

        _targetYaw += yaw * 70.0f * deltaTime;
        _targetPitch += pitch * 50.0f * deltaTime;
        _targetDistance *= MathF.Pow(0.25f, zoom * deltaTime);
        return true;

        static float Axis(Key a, Key b) => Input.GetKey(a) || Input.GetKey(b) ? 1.0f : 0.0f;
    }

    private void Apply()
    {
        _transform.Position = _pivot + _distance * Offset(_yaw, _pitch);
        _transform.Rotation = new Vector3(-_pitch, _yaw, 0.0f);

        // A near plane that scales with the distance keeps depth precise from a close-up to the whole system.
        if (_camera is not null)
        {
            float near = Math.Clamp(_distance * 0.01f, 0.01f, 1.0f);
            if (MathF.Abs(_camera.NearClip - near) > near * 0.05f)
            {
                _camera.NearClip = near;
            }
        }
    }

    // Rings seen edge-on are a line; nudge the view, as little as possible, until they open up to at least ~24°.
    private void FrameRings(Vector3 pole)
    {
        const float minimumOpening = 0.4f; // sin(24°)
        if (MathF.Abs(Vector3.Dot(Offset(_targetYaw, _targetPitch), pole)) >= minimumOpening)
        {
            return;
        }

        float bestCost = float.MaxValue;
        float bestYaw = _targetYaw;
        float bestPitch = _targetPitch;
        for (float yaw = -90.0f; yaw <= 90.0f; yaw += 10.0f)
        {
            // Stay on the same side of the orbital plane, and off it, so the orbits keep reading as ellipses.
            for (float pitch = 10.0f; pitch <= 60.0f; pitch += 5.0f)
            {
                float candidate = _targetPitch < 0.0f ? -pitch : pitch;
                if (MathF.Abs(Vector3.Dot(Offset(_targetYaw + yaw, candidate), pole)) < minimumOpening) continue;
                float cost = MathF.Abs(yaw) + MathF.Abs(candidate - _targetPitch) * 1.5f;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestYaw = _targetYaw + yaw;
                    bestPitch = candidate;
                }
            }
        }

        _targetYaw = bestYaw;
        _targetPitch = bestPitch;
    }

    private static Vector3 Offset(float yawDegrees, float pitchDegrees)
    {
        float yaw = yawDegrees * (MathF.PI / 180.0f);
        float pitch = pitchDegrees * (MathF.PI / 180.0f);
        return new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
    }

    private static float FocusDistance(CelestialBody body) =>
        body.Kind == BodyKind.Star ? body.Radius * 5.0f : body.VisualRadius * 4.2f;
}
