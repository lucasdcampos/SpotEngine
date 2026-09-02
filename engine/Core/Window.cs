using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Spot.Events;
using SilkWindow = Silk.NET.Windowing.Window;

namespace Spot.Core;

/// <summary>
/// Describes how a <see cref="Window"/> should be created.
/// </summary>
public class WindowSpec
{
    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    public string Title { get; set; } = "Spot Window";

    /// <summary>
    /// Gets or sets the window width in pixels.
    /// </summary>
    public int Width { get; set; } = 1280;

    /// <summary>
    /// Gets or sets the window height in pixels.
    /// </summary>
    public int Height { get; set; } = 720;

    /// <summary>
    /// Gets or sets the path to the window icon.
    /// </summary>
    public string? IconPath { get; set; }
}

/// <summary>
/// A platform window backed by Silk.NET.
/// </summary>
public sealed class Window : IDisposable
{
    private readonly WindowSpec _spec;
    private readonly IWindow _window;
    private readonly IInputContext _input;

    private int _width;
    private int _height;
    private EventCallback? _callback;

    /// <summary>
    /// Initializes a new instance of the <see cref="Window"/> class.
    /// </summary>
    /// <param name="spec">The window specification.</param>
    public Window(WindowSpec spec)
    {
        _spec = spec;
        _width = spec.Width;
        _height = spec.Height;
        Display.SetSize(_width, _height);

        WindowOptions options = WindowOptions.Default;
        options.Title = spec.Title;
        options.Size = new Vector2D<int>(spec.Width, spec.Height);
        options.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.Default,
            new APIVersion(4, 6));
        options.WindowBorder = WindowBorder.Resizable;
        options.VSync = Spot.Rendering.RenderSettings.VSync;

        _window = SilkWindow.Create(options);
        _window.Initialize();

        if (!string.IsNullOrEmpty(spec.IconPath) && System.IO.File.Exists(spec.IconPath))
        {
            try
            {
                using var stream = System.IO.File.OpenRead(spec.IconPath);
                var image = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                var rawImage = new Silk.NET.Core.RawImage(image.Width, image.Height, image.Data);
                _window.SetWindowIcon(ref rawImage);
            }
            catch (Exception ex)
            {
                Log.CoreWarn("Failed to load window icon '{0}': {1}", spec.IconPath, ex.Message);
            }
        }

        // Center the window on the primary monitor by default. Hosts that manage their own window
        // placement (e.g. the editor restoring a saved layout) re-apply their position afterwards.
        try
        {
            _window.Center();
        }
        catch
        {
            // Centering is best-effort; never let window placement take the process down.
        }

        _input = _window.CreateInput();
        // Centre in the mouse-position coordinate space (the window's client size, matching IMouse.Position)
        // so recentring keeps the cursor comfortably inside the window regardless of DPI/framebuffer scale.
        global::Spot.Core.Input.CursorController = new SilkCursorController(
            _input, () => new System.Numerics.Vector2(_window.Size.X / 2f, _window.Size.Y / 2f));
        SetupCallbacks();

        // Apply engine-wide VSync changes to this window at runtime (the `vsync` console command, an editor
        // toggle, or a game turning it off to profile). Unsubscribed on Dispose so the static event never
        // pins a disposed window.
        Spot.Rendering.RenderSettings.VSyncChanged += OnVSyncChanged;

        Log.CoreInfo("Window '{0}' created ({1}x{2})", spec.Title, spec.Width, spec.Height);
    }

    /// <summary>
    /// Gets the window width in pixels.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Gets the window height in pixels.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    public string Title
    {
        get => _window.Title;
        set => _window.Title = value;
    }

    /// <summary>
    /// Gets or sets whether presentation waits for vertical sync on this window. Setting it updates the
    /// swap interval immediately. Prefer <see cref="Spot.Rendering.RenderSettings.VSync"/> as the
    /// engine-wide source of truth; it flows here automatically.
    /// </summary>
    public bool VSync
    {
        get => _window.VSync;
        set => _window.VSync = value;
    }

    /// <summary>
    /// Gets the underlying Silk.NET window.
    /// </summary>
    public IWindow NativeWindow => _window;

    /// <summary>
    /// Gets the input context associated with this window.
    /// </summary>
    public IInputContext Input => _input;

    /// <summary>
    /// Sets the callback invoked when the window raises an event.
    /// </summary>
    /// <param name="callback">The event callback.</param>
    public void SetEventCallback(EventCallback callback) => _callback = callback;

    /// <summary>
    /// Processes pending window and input events.
    /// </summary>
    public void PollEvents() => _window.DoEvents();

    /// <summary>
    /// Swaps the front and back buffers, presenting the rendered frame.
    /// </summary>
    public void SwapBuffers() => _window.SwapBuffers();

    /// <summary>
    /// Gets a value indicating whether the window has been requested to close.
    /// </summary>
    /// <returns><see langword="true"/> if the window should close; otherwise, <see langword="false"/>.</returns>
    public bool ShouldClose() => _window.IsClosing;

    /// <summary>
    /// Cancels a pending close request, keeping the window open (for example after the user declines
    /// to close with unsaved changes).
    /// </summary>
    public void CancelClose() => _window.IsClosing = false;

    /// <inheritdoc />
    public void Dispose()
    {
        Spot.Rendering.RenderSettings.VSyncChanged -= OnVSyncChanged;
        _input.Dispose();
        _window.DoEvents();
        _window.Reset();
        _window.Dispose();
    }

    // Pushes an engine-wide VSync change onto the Silk window. Guarded so a backend that rejects a late
    // swap-interval change logs and continues rather than taking the process down (never crash the engine).
    private void OnVSyncChanged(bool enabled)
    {
        try
        {
            _window.VSync = enabled;
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to apply VSync change to the window: {0}", ex.Message);
        }
    }

    private void SetupCallbacks()
    {
        _window.Closing += () => _callback?.Invoke(new WindowCloseEvent());

        _window.Resize += size =>
        {
            _width = size.X;
            _height = size.Y;
            Display.SetSize(_width, _height);
            _callback?.Invoke(new WindowResizeEvent(size.X, size.Y));
        };

        _window.FileDrop += paths => _callback?.Invoke(new WindowDropEvent(paths));

        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => _callback?.Invoke(new KeyPressedEvent((Key)(int)key));
            keyboard.KeyUp += (_, key, _) => _callback?.Invoke(new KeyReleasedEvent((Key)(int)key));
            keyboard.KeyChar += (_, character) => _callback?.Invoke(new KeyTypedEvent(character));
        }

        foreach (IMouse mouse in _input.Mice)
        {
            mouse.MouseMove += (_, position) => _callback?.Invoke(new MouseMovedEvent(position.X, position.Y));
            mouse.Scroll += (_, wheel) => _callback?.Invoke(new MouseScrolledEvent(wheel.X, wheel.Y));
            mouse.MouseDown += (_, button) => _callback?.Invoke(new MouseButtonPressedEvent((MouseButton)(int)button));
            mouse.MouseUp += (_, button) => _callback?.Invoke(new MouseButtonReleasedEvent((MouseButton)(int)button));
        }

        _input.ConnectionChanged += (device, connected) =>
        {
            if (device is IGamepad gamepad)
            {
                if (connected)
                {
                    SetupGamepad(gamepad);
                    _callback?.Invoke(new GamepadConnectedEvent(gamepad.Index));
                }
                else
                {
                    _callback?.Invoke(new GamepadDisconnectedEvent(gamepad.Index));
                }
            }
        };

        foreach (IGamepad gamepad in _input.Gamepads)
        {
            SetupGamepad(gamepad);
        }
    }

    private void SetupGamepad(IGamepad gamepad)
    {
        gamepad.ButtonDown += (gp, button) => _callback?.Invoke(new GamepadButtonPressedEvent(gp.Index, MapGamepadButton(button.Name)));
        gamepad.ButtonUp += (gp, button) => _callback?.Invoke(new GamepadButtonReleasedEvent(gp.Index, MapGamepadButton(button.Name)));
        
        gamepad.ThumbstickMoved += (gp, thumbstick) => 
        {
            if (thumbstick.Index == 0)
            {
                _callback?.Invoke(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.LeftX, thumbstick.X));
                _callback?.Invoke(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.LeftY, thumbstick.Y));
            }
            else if (thumbstick.Index == 1)
            {
                _callback?.Invoke(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.RightX, thumbstick.X));
                _callback?.Invoke(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.RightY, thumbstick.Y));
            }
        };

        gamepad.TriggerMoved += (gp, trigger) =>
        {
            if (trigger.Index == 0)
            {
                _callback?.Invoke(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.LeftTrigger, trigger.Position));
            }
            else if (trigger.Index == 1)
            {
                _callback?.Invoke(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.RightTrigger, trigger.Position));
            }
        };
    }

    // Drives the hardware cursor through the window's Silk mouse, letting Input lock/unlock it without
    // depending on Silk. Locking hides the cursor and confines it *manually*: every frame Tick() reads how
    // far it drifted from the window centre, reports that as relative motion, then warps it back. GLFW's
    // own Raw/Disabled "confine" modes proved unreliable (they read back as applied while the OS cursor
    // still roamed free and escaped the window), so we recentre it ourselves — the backend-independent
    // way to guarantee the cursor stays put during mouse-look.
    private sealed class SilkCursorController : ICursorController
    {
        private readonly IInputContext _input;
        private readonly Func<System.Numerics.Vector2> _windowCenter;
        private bool _locked;
        private bool _justLocked;
        private System.Numerics.Vector2 _unlockPosition;

        public SilkCursorController(IInputContext input, Func<System.Numerics.Vector2> windowCenter)
        {
            _input = input;
            _windowCenter = windowCenter;
        }

        public bool Locked
        {
            get => _locked;

            set
            {
                if (value == _locked)
                {
                    return;
                }

                _locked = value;
                IMouse? mouse = _input.Mice.Count > 0 ? _input.Mice[0] : null;
                if (value)
                {
                    // Remember where to restore the cursor to on unlock, then hide it and let Tick() take
                    // over confinement from the next frame.
                    if (mouse != null)
                    {
                        _unlockPosition = mouse.Position;
                        mouse.Cursor.CursorMode = CursorMode.Hidden;
                    }
                    _justLocked = true;
                }
                else if (mouse != null)
                {
                    mouse.Cursor.CursorMode = CursorMode.Normal;
                    mouse.Position = _unlockPosition; // reappear where the lock began, not parked at centre
                }

                global::Spot.Core.Input.RelativeMouseMode = value;
            }
        }

        public void Tick()
        {
            if (!_locked || _input.Mice.Count == 0)
            {
                return;
            }

            IMouse mouse = _input.Mice[0];
            // Keep it hidden in case anything (e.g. the ImGui overlay) reset the cursor this frame.
            if (mouse.Cursor.CursorMode != CursorMode.Hidden)
            {
                mouse.Cursor.CursorMode = CursorMode.Hidden;
            }

            System.Numerics.Vector2 center = _windowCenter();
            if (_justLocked)
            {
                // First locked frame: just centre the cursor; the offset from the press point isn't motion.
                mouse.Position = center;
                _justLocked = false;
                return;
            }

            System.Numerics.Vector2 delta = mouse.Position - center;
            if (delta != System.Numerics.Vector2.Zero)
            {
                global::Spot.Core.Input.AddMouseMotion(delta);
                mouse.Position = center; // snap back so the next frame's offset is pure movement
            }
        }
    }

    private static GamepadButton MapGamepadButton(ButtonName name)
    {
        return name switch
        {
            ButtonName.A => GamepadButton.A,
            ButtonName.B => GamepadButton.B,
            ButtonName.X => GamepadButton.X,
            ButtonName.Y => GamepadButton.Y,
            ButtonName.LeftBumper => GamepadButton.LeftBumper,
            ButtonName.RightBumper => GamepadButton.RightBumper,
            ButtonName.Back => GamepadButton.Back,
            ButtonName.Start => GamepadButton.Start,
            ButtonName.Home => GamepadButton.Guide,
            ButtonName.LeftStick => GamepadButton.LeftThumb,
            ButtonName.RightStick => GamepadButton.RightThumb,
            ButtonName.DPadUp => GamepadButton.DPadUp,
            ButtonName.DPadRight => GamepadButton.DPadRight,
            ButtonName.DPadDown => GamepadButton.DPadDown,
            ButtonName.DPadLeft => GamepadButton.DPadLeft,
            _ => GamepadButton.A
        };
    }
}
