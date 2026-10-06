using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Engine.UI;
using Spot.Engine;

namespace SolarSystem;

/// <summary>
/// The interface: hover picking, clicks and keyboard shortcuts, the guided tour, and the HUD. The title is a UI
/// document (<c>Assets/UI/Hud.sptui</c>, on this entity's UI Canvas); the icon toolbar and its settings pop-up are
/// built in code (<see cref="HudControls"/>) from engine widgets restyled by subclassing; everything that follows
/// the 3D view is the <see cref="SolarOverlay"/> widget.
/// </summary>
public sealed class SolarHud : Component
{
    /// <summary>Gets or sets the idle time after which the guided tour starts on its own, in seconds (0 = never).</summary>
    [InspectorRange(0.0f, 600.0f, 1.0f)]
    public float AutoTourAfter { get; set; } = 90.0f;

    /// <summary>Gets or sets how long the tour stays at each body, in seconds.</summary>
    [InspectorRange(2.0f, 60.0f, 0.5f)]
    public float TourDwell { get; set; } = 11.0f;

    public bool ShowLabels { get; set; } = true;

    private static readonly Key[] CameraKeys = { Key.W, Key.A, Key.S, Key.D, Key.Q, Key.E, Key.Up, Key.Down, Key.Left, Key.Right };

    private readonly List<Widget> _documentWidgets = new();
    private Simulation? _simulation;
    private OrbitCamera? _camera;
    private SpaceRenderer? _renderer;
    private AsteroidBelt? _belt;
    private PostProcessing? _post;
    private HudKit? _kit;
    private SolarOverlay? _overlay;
    private HudControls? _controls;

    private bool _pressStartedOnUI;
    private Vector2 _lastMouse;
    private float _idle;
    private bool _touring;
    private int _tourStop;
    private float _tourTimer;
    private bool _helpOpen;
    private bool _autoTour;
    private float _toastTimer;

    /// <summary>Gets every body, in tour order.</summary>
    internal IReadOnlyList<CelestialBody> Bodies => _simulation?.Bodies ?? Array.Empty<CelestialBody>();

    /// <summary>Gets the body under the pointer.</summary>
    internal CelestialBody? Hovered { get; private set; }

    /// <summary>Gets the body whose card stays open — the last one clicked or chosen.</summary>
    internal CelestialBody? Pinned { get; private set; }

    /// <summary>Gets the body the info card describes: the hovered one, else the pinned one.</summary>
    internal CelestialBody? CardBody => Hovered ?? Pinned;

    internal bool InterfaceHidden { get; private set; }

    internal float FrameTime { get; private set; }

    public override void OnStart()
    {
        _simulation = Simulation.Current;
        _camera = Scene.GetComponents<OrbitCamera>().FirstOrDefault();
        _renderer = Scene.GetComponents<SpaceRenderer>().FirstOrDefault();
        _belt = Scene.GetComponents<AsteroidBelt>().FirstOrDefault();

        foreach (Entity entity in Scene.View<PostProcessing>())
        {
            _post ??= entity.GetComponent<PostProcessing>();
        }

        _autoTour = AutoTourAfter > 0.0f;
        _documentWidgets.AddRange(UI.Children);
        _kit = new HudKit();
        _overlay = UI.Add(new SolarOverlay(this, _kit));
        _controls = new HudControls(UI, _kit);
        Wire(_controls);
        ShowToast("Hover a planet to learn about it", 5.0f);
    }

    // The widgets this script added go with it (the document's belong to the UI Canvas), then their textures.
    public override void OnDestroy()
    {
        if (_overlay is not null) UI.Remove(_overlay);
        if (_controls is not null)
        {
            UI.Remove(_controls.Toolbar);
            UI.Remove(_controls.Settings);
        }

        _kit?.Dispose();
    }

    public override void OnUpdate(float deltaTime)
    {
        FrameTime = deltaTime;
        _simulation ??= Simulation.Current;
        bool input = HandleKeys();
        input |= HandlePointer();

        _idle = input || Input.MouseScrollDelta != Vector2.Zero ? 0.0f : _idle + deltaTime;
        if (!_touring && _autoTour && AutoTourAfter > 0.0f && _idle > AutoTourAfter)
        {
            StartTour();
        }

        UpdateTour(deltaTime);
        UpdateWidgets(deltaTime);
    }

    /// <summary>
    /// Gets the primary camera's view-projection, position and focal length (the projection's vertical scale).
    /// </summary>
    internal bool TryGetView(out Matrix4x4 viewProjection, out Vector3 position, out float focal)
    {
        viewProjection = default;
        position = default;
        focal = 1.0f;
        if (!Scene.TryGetActivePrimaryCamera(out Entity camera))
        {
            return false;
        }

        Camera component = camera.GetComponent<Camera>();
        Transform transform = camera.GetComponent<Transform>();
        viewProjection = component.GetViewProjection(transform);
        position = transform.WorldPosition;
        focal = component.Projection.M22;
        return true;
    }

    private bool HandleKeys()
    {
        bool shift = Input.GetKey(Key.LeftShift) || Input.GetKey(Key.RightShift);
        bool any = false;

        if (Input.GetKeyDown(Key.F1) || (Input.GetKeyDown(Key.Slash) && shift))
        {
            _helpOpen = !_helpOpen;
            any = true;
        }

        // Esc backs out one step at a time: the pop-up, then the shortcut sheet, then the focused body.
        if (Input.GetKeyDown(Key.Escape) || Input.GetKeyDown(Key.Backspace))
        {
            if (_controls is { SettingsOpen: true })
            {
                _controls.SettingsOpen = false;
            }
            else if (_helpOpen)
            {
                _helpOpen = false;
            }
            else
            {
                GoToOverview();
            }

            any = true;
        }

        if (Input.GetKeyDown(Key.H))
        {
            InterfaceHidden = !InterfaceHidden;
            foreach (Widget widget in _documentWidgets)
            {
                widget.Visible = !InterfaceHidden;
            }

            if (_controls is not null)
            {
                _controls.Shown = !InterfaceHidden;
                _controls.SettingsOpen = false;
            }

            if (InterfaceHidden)
            {
                ShowToast("Interface hidden  ·  press H to bring it back", 2.5f);
            }

            any = true;
        }

        // With no readouts on screen, each key confirms what it changed with a brief notice.
        if (Input.GetKeyDown(Key.Space))
        {
            TogglePause();
            ShowToast(_simulation is { Paused: true } ? "Paused" : "Resumed", 1.2f);
            any = true;
        }

        if (Input.GetKeyDown(Key.Comma) || Input.GetKeyDown(Key.Period))
        {
            if (Input.GetKeyDown(Key.Comma)) _simulation?.Slower();
            else _simulation?.Faster();
            ShowToast(_simulation?.DescribeSpeed() ?? "", 1.2f);
            any = true;
        }

        if (Input.GetKeyDown(Key.O))
        {
            ToggleOrbits();
            ShowToast(_renderer is { ShowOrbits: true } ? "Orbits shown" : "Orbits hidden", 1.2f);
            any = true;
        }

        if (Input.GetKeyDown(Key.L))
        {
            ShowLabels = !ShowLabels;
            ShowToast(ShowLabels ? "Labels shown" : "Labels hidden", 1.2f);
            any = true;
        }

        if (Input.GetKeyDown(Key.T)) { ToggleTour(); any = true; }

        // Choosing a body by key: 0 is the Sun, 1-8 the planets, M the Moon, Tab steps through them all.
        CelestialBody? chosen = null;
        for (int n = 0; n <= 8; n++)
        {
            if (Input.GetKeyDown(Key.Alpha0 + n))
            {
                chosen = Bodies.FirstOrDefault(b => b.Kind != BodyKind.Moon && b.Order == n);
            }
        }

        if (Input.GetKeyDown(Key.M))
        {
            chosen = Bodies.FirstOrDefault(b => b.Kind == BodyKind.Moon);
        }

        if (Input.GetKeyDown(Key.Tab) && Bodies.Count > 0)
        {
            int current = Pinned is null ? -1 : IndexOf(Pinned);
            int next = shift ? (current <= 0 ? Bodies.Count - 1 : current - 1) : (current + 1) % Bodies.Count;
            chosen = Bodies[next];
        }

        if (Input.GetKeyDown(Key.F) && (Hovered ?? Pinned) is { } target)
        {
            chosen = target;
        }

        if (chosen is not null)
        {
            StopTour();
            Select(chosen);
            any = true;
        }

        // Camera keys count as activity too, so the tour doesn't start while someone flies around.
        foreach (Key key in CameraKeys)
        {
            if (Input.GetKey(key))
            {
                any = true;
            }
        }

        return any;
    }

    private bool HandlePointer()
    {
        bool overUI = UI.WantsPointer;
        if (_camera is not null)
        {
            _camera.MouseBlocked = overUI;
        }

        // Moving the mouse counts as someone being there, so the tour doesn't take over while a card is being read.
        Vector2 mouse = Input.MousePosition;
        bool active = Vector2.DistanceSquared(mouse, _lastMouse) > 1.0f;
        _lastMouse = mouse;
        if (Input.GetMouseButtonDown(MouseButton.Left) || Input.GetMouseButtonDown(MouseButton.Right))
        {
            _pressStartedOnUI = overUI;

            // A click outside the open pop-up only closes it.
            if (_controls is { SettingsOpen: true } && UI.Scale > 0.0f && !_controls.Contains(mouse / UI.Scale))
            {
                _controls.SettingsOpen = false;
                _pressStartedOnUI = true;
            }
            else if (!overUI)
            {
                StopTour();
            }

            active = true;
        }

        // While the game doesn't have the input (an overlay or the editor holds it), the frozen pointer hovers nothing.
        bool dragging = _camera?.IsDragging ?? false;
        bool blocked = overUI || dragging || _helpOpen || InterfaceHidden || Input.Suppressed || Input.Captured;
        Hovered = blocked ? null : Pick(Input.MousePosition);
        if (_renderer is not null)
        {
            _renderer.Highlighted = Hovered ?? Pinned;
        }

        // A click is a press and release that didn't turn into a drag: it focuses what is under the pointer, or
        // clears the pinned card when it lands on empty space.
        if (Input.GetMouseButtonUp(MouseButton.Left) && !_pressStartedOnUI && !dragging && !_helpOpen)
        {
            if (Hovered is { } body)
            {
                Select(body);
            }
            else
            {
                Pinned = null;
            }

            active = true;
        }

        return active || dragging;
    }

    // The body whose disc (or a generous minimum around a speck) is under the pointer, preferring the nearest
    // center relative to its size — so a small moon in front of its planet can still be picked.
    private CelestialBody? Pick(Vector2 mouse)
    {
        if (!TryGetView(out Matrix4x4 viewProjection, out Vector3 camera, out float focal) || UI.Scale <= 0.0f)
        {
            return null;
        }

        Vector2 pointer = mouse / UI.Scale;
        var size = new Vector2(UI.Width, UI.Height);
        CelestialBody? best = null;
        float bestScore = 1.0f;
        foreach (CelestialBody body in Bodies)
        {
            Vector4 clip = Vector4.Transform(new Vector4(body.Position, 1.0f), viewProjection);
            if (clip.W <= 1e-4f) continue;
            var at = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * size.X, (0.5f - clip.Y / clip.W * 0.5f) * size.Y);
            float d = Vector3.Distance(body.Position, camera);
            float radius = body.Radius / MathF.Sqrt(MathF.Max(d * d - body.Radius * body.Radius, 1e-6f)) * focal * size.Y * 0.5f;
            float reach = MathF.Max(radius * 1.1f, 14.0f);
            float score = Vector2.Distance(pointer, at) / reach;
            if (score < bestScore)
            {
                bestScore = score;
                best = body;
            }
        }

        return best;
    }

    private void GoToOverview()
    {
        StopTour();
        Pinned = null;
        _camera?.Overview();
    }

    private void Select(CelestialBody body)
    {
        Pinned = body;
        _camera?.Focus(body);
    }

    private void TogglePause()
    {
        if (_simulation is not null)
        {
            _simulation.Paused = !_simulation.Paused;
        }
    }

    private void ToggleOrbits()
    {
        if (_renderer is not null)
        {
            _renderer.ShowOrbits = !_renderer.ShowOrbits;
        }
    }

    private void ToggleTour()
    {
        if (_touring)
        {
            StopTour();
        }
        else
        {
            StartTour();
        }
    }

    private void StartTour()
    {
        if (Bodies.Count == 0)
        {
            return;
        }

        _touring = true;
        _tourStop = 0;
        _tourTimer = 0.0f;
        _idle = 0.0f;
        GoToStop();
        ShowToast("Guided tour  ·  click or press T to take over", 3.5f);
    }

    private void StopTour()
    {
        if (!_touring)
        {
            return;
        }

        _touring = false;
        if (_camera is not null)
        {
            _camera.AutoOrbit = 0.0f;
        }
    }

    // The tour visits every body in order, then pulls back to the overview before starting again.
    private void UpdateTour(float deltaTime)
    {
        if (!_touring)
        {
            return;
        }

        _tourTimer += deltaTime;
        bool overview = _tourStop >= Bodies.Count;
        if (_tourTimer >= (overview ? TourDwell * 0.8f : TourDwell))
        {
            _tourTimer = 0.0f;
            _tourStop = overview ? 0 : _tourStop + 1;
            GoToStop();
        }
    }

    private void GoToStop()
    {
        if (_camera is null)
        {
            return;
        }

        _camera.AutoOrbit = 4.0f;
        if (_tourStop < Bodies.Count)
        {
            Select(Bodies[_tourStop]);
        }
        else
        {
            Pinned = null;
            _camera.Overview();
        }
    }

    // What the controls do. A switch reports the value it flipped to; UpdateWidgets keeps it in step when a key
    // changes the same setting.
    private void Wire(HudControls controls)
    {
        controls.Pause.OnClick += TogglePause;
        controls.Overview.OnClick += GoToOverview;
        controls.Tour.OnClick += ToggleTour;
        controls.Gear.OnClick += () => controls.SettingsOpen = !controls.SettingsOpen;
        controls.Close.OnClick += () => controls.SettingsOpen = false;
        controls.Shortcuts.OnClick += () =>
        {
            controls.SettingsOpen = false;
            _helpOpen = true;
        };

        controls.Speed.Min = 0.0f;
        controls.Speed.Max = Simulation.Speeds.Length - 1;
        controls.Speed.Steps = Simulation.Speeds.Length;
        controls.Speed.OnValueChanged += value =>
        {
            if (_simulation is not null)
            {
                _simulation.DaysPerSecond = Simulation.Speeds[Math.Clamp((int)MathF.Round(value), 0, Simulation.Speeds.Length - 1)];
            }
        };

        controls.Orbits.OnValueChanged += on =>
        {
            if (_renderer is not null) _renderer.ShowOrbits = on;
        };
        controls.Labels.OnValueChanged += on => ShowLabels = on;
        controls.Asteroids.OnValueChanged += on =>
        {
            if (_belt is not null)
            {
                Entity belt = _belt.Entity;
                belt.Enabled = on;
            }
        };
        controls.Bloom.OnValueChanged += on =>
        {
            if (_post is not null) _post.EnableBloom = on;
        };
        controls.AutoTour.OnValueChanged += on => _autoTour = on;
    }

    private void UpdateWidgets(float deltaTime)
    {
        if (_controls is { } controls && _kit is { } kit)
        {
            bool paused = _simulation?.Paused ?? false;
            controls.Pause.Icon = paused ? kit.PlayIcon : kit.PauseIcon;
            controls.Pause.Tooltip = paused ? "Resume" : "Pause";
            controls.Tour.Active = _touring;
            controls.Tour.Tooltip = _touring ? "Stop the tour" : "Guided tour";

            if (_simulation is { } simulation)
            {
                controls.SpeedValue.Content = simulation.DescribeSpeed();

                // While dragged the knob follows the pointer; let go, it settles on the chosen step.
                if (!controls.Speed.Dragging)
                {
                    controls.Speed.Value = SpeedIndex(simulation.DaysPerSecond);
                }
            }

            controls.Orbits.On = _renderer?.ShowOrbits ?? false;
            controls.Labels.On = ShowLabels;
            controls.Asteroids.On = _belt?.Entity.Enabled ?? false;
            controls.Bloom.On = _post?.EnableBloom ?? false;
            controls.AutoTour.On = _autoTour;
            controls.Animate(deltaTime);
        }

        if (_overlay is not null)
        {
            _overlay.HelpAlpha = Approach(_overlay.HelpAlpha, _helpOpen ? 1.0f : 0.0f, deltaTime * 6.0f);
            _toastTimer -= deltaTime;
            _overlay.ToastAlpha = Approach(_overlay.ToastAlpha, _toastTimer > 0.0f ? 1.0f : 0.0f, deltaTime * 4.0f);
        }
    }

    // The step of Simulation.Speeds closest to a speed.
    private static int SpeedIndex(float daysPerSecond)
    {
        int best = 0;
        for (int i = 1; i < Simulation.Speeds.Length; i++)
        {
            if (MathF.Abs(Simulation.Speeds[i] - daysPerSecond) < MathF.Abs(Simulation.Speeds[best] - daysPerSecond)) best = i;
        }

        return best;
    }

    private void ShowToast(string message, float seconds)
    {
        if (_overlay is not null)
        {
            _overlay.Toast = message;
        }

        _toastTimer = seconds;
    }

    private int IndexOf(CelestialBody body)
    {
        for (int i = 0; i < Bodies.Count; i++)
        {
            if (Bodies[i] == body) return i;
        }

        return -1;
    }


    private static float Approach(float value, float target, float step) =>
        value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);
}
