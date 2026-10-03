using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Spot.Framework.Events;
using Spot.Framework.Graphics;
using SilkWindow = Silk.NET.Windowing.Window;

namespace Spot.Framework;

/// <summary>
/// A platform window with an OpenGL context, backed by Silk.NET. Creating one is all it takes to start drawing:
/// it installs its context as the <see cref="Renderer"/>'s device and feeds every event into <see cref="Input"/>.
/// The window does not run a loop — drive it yourself:
/// <code>
/// using var window = new Window(new WindowSpec { Title = "Hello" });
/// while (window.IsOpen)
/// {
///     window.PollEvents();
///     Renderer.Clear();
///     // draw...
///     window.SwapBuffers();
/// }
/// </code>
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
        options.VSync = spec.VSync;

        _window = SilkWindow.Create(options);
        _window.Initialize();

        if (spec.Icon is { } icon)
        {
            SetIcon(icon);
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
        global::Spot.Framework.Input.CursorController = new SilkCursorController(
            _input, () => new System.Numerics.Vector2(_window.Size.X / 2f, _window.Size.Y / 2f));
        SetupCallbacks();

        // The window's context becomes the renderer's device, so drawing works as soon as the window exists.
        Renderer.Init(GL.GetApi(_window));

        // Make sure the drawable size has reached the renderer before the first frame (see ForceInitialResize).
        ForceInitialResize();
        SyncViewport();
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
    /// Gets the drawable width in physical pixels (larger than <see cref="Width"/> under DPI scaling).
    /// </summary>
    public int FramebufferWidth => _window.FramebufferSize.X > 0 ? _window.FramebufferSize.X : _window.Size.X;

    /// <summary>
    /// Gets the drawable height in physical pixels (larger than <see cref="Height"/> under DPI scaling).
    /// </summary>
    public int FramebufferHeight => _window.FramebufferSize.Y > 0 ? _window.FramebufferSize.Y : _window.Size.Y;

    /// <summary>
    /// Gets whether the window is open: <see langword="false"/> once a close was requested (by the user or
    /// <see cref="Close"/>) and not cancelled with <see cref="CancelClose"/>. The condition for a main loop.
    /// </summary>
    public bool IsOpen => !_window.IsClosing;

    /// <summary>
    /// Gets or sets whether presentation waits for vertical sync on this window. Setting it updates the
    /// swap interval immediately; a backend that rejects the change logs and keeps the old setting.
    /// </summary>
    public bool VSync
    {
        get => _window.VSync;
        set
        {
            try
            {
                _window.VSync = value;
            }
            catch (Exception ex)
            {
                Log.CoreWarn("Failed to apply VSync change to the window: {0}", ex.Message);
            }
        }
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
    /// Processes pending window and input events for a new frame: starts a new <see cref="Input"/> frame, feeds
    /// every event to <see cref="Input"/> and then to the event callback, and recentres a locked cursor.
    /// </summary>
    public void PollEvents()
    {
        Spot.Framework.Input.NewFrame();
        _window.DoEvents();

        // After the frame's mouse events are in, recentre a locked cursor and turn its drift into relative
        // motion, before the app reads Input.MousePosition. Keeps mouse-look confined to the window.
        Spot.Framework.Input.TickCursorLock();

        // Safety net: if the drawable size still hasn't reached the renderer (the startup resize was deferred
        // by the platform), re-sync it so no frame renders into a 0-sized viewport.
        if (Renderer.ViewportWidth == 0 || Renderer.ViewportHeight == 0)
        {
            SyncViewport();
        }
    }

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

    /// <summary>
    /// Requests the window to close; <see cref="IsOpen"/> becomes <see langword="false"/>.
    /// </summary>
    public void Close() => _window.IsClosing = true;

    /// <summary>
    /// Sets the window icon from raw pixels. A failure logs and keeps the current icon.
    /// </summary>
    /// <param name="icon">The icon pixels, rows top-to-bottom.</param>
    public void SetIcon(WindowIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        try
        {
            if (icon.Rgba.Length != icon.Width * icon.Height * 4)
            {
                throw new ArgumentException("Icon pixel data does not match its size.", nameof(icon));
            }

            var raw = new Silk.NET.Core.RawImage(icon.Width, icon.Height, icon.Rgba);
            _window.SetWindowIcon(ref raw);
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to set the window icon: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Points the renderer's viewport at the window's whole drawable. Done automatically on resize; call it to
    /// restore the screen viewport after rendering elsewhere.
    /// </summary>
    public void SyncViewport()
    {
        int w = FramebufferWidth;
        int h = FramebufferHeight;
        if (w > 0 && h > 0)
        {
            Renderer.SetViewport(0, 0, (uint)w, (uint)h);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _input.Dispose();
        _window.DoEvents();
        _window.Reset();
        _window.Dispose();
    }

    // Works around a startup quirk: with a manual loop (Initialize + DoEvents rather than IWindow.Run), some
    // platforms leave IWindow.FramebufferSize reporting 0 until the first real resize. That zero collapses the
    // GL viewport (and any UI layer's framebuffer scale), so nothing draws and the window shows only the clear
    // color until the user resizes it. Nudging the size by a pixel and back drives Silk's resize pipeline once —
    // populating the framebuffer size — which is exactly what a manual resize does.
    private void ForceInitialResize()
    {
        try
        {
            var size = _window.Size;
            if (size.X > 0 && size.Y > 0 && (_window.FramebufferSize.X == 0 || _window.FramebufferSize.Y == 0))
            {
                _window.Size = new Vector2D<int>(size.X, size.Y + 1);
                _window.DoEvents();
                _window.Size = size;
                _window.DoEvents();
            }
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Initial window resize sync failed: {0}", ex.Message);
        }
    }

    // Feeds an event to Input first (so polled state is current), then to the app's callback.
    private void Dispatch(Event e)
    {
        Spot.Framework.Input.OnEvent(e);
        _callback?.Invoke(e);
    }

    private void SetupCallbacks()
    {
        _window.Closing += () => Dispatch(new WindowCloseEvent());

        _window.Resize += size =>
        {
            _width = size.X;
            _height = size.Y;
            Display.SetSize(_width, _height);
            SyncViewport();
            Dispatch(new WindowResizeEvent(size.X, size.Y));
        };

        _window.FileDrop += paths => Dispatch(new WindowDropEvent(paths));

        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => Dispatch(new KeyPressedEvent((Key)(int)key));
            keyboard.KeyUp += (_, key, _) => Dispatch(new KeyReleasedEvent((Key)(int)key));
            keyboard.KeyChar += (_, character) => Dispatch(new KeyTypedEvent(character));
        }

        foreach (IMouse mouse in _input.Mice)
        {
            mouse.MouseMove += (_, position) => Dispatch(new MouseMovedEvent(position.X, position.Y));
            mouse.Scroll += (_, wheel) => Dispatch(new MouseScrolledEvent(wheel.X, wheel.Y));
            mouse.MouseDown += (_, button) => Dispatch(new MouseButtonPressedEvent((MouseButton)(int)button));
            mouse.MouseUp += (_, button) => Dispatch(new MouseButtonReleasedEvent((MouseButton)(int)button));
        }

        _input.ConnectionChanged += (device, connected) =>
        {
            if (device is IGamepad gamepad)
            {
                if (connected)
                {
                    SetupGamepad(gamepad);
                    Dispatch(new GamepadConnectedEvent(gamepad.Index));
                }
                else
                {
                    Dispatch(new GamepadDisconnectedEvent(gamepad.Index));
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
        gamepad.ButtonDown += (gp, button) => Dispatch(new GamepadButtonPressedEvent(gp.Index, MapGamepadButton(button.Name)));
        gamepad.ButtonUp += (gp, button) => Dispatch(new GamepadButtonReleasedEvent(gp.Index, MapGamepadButton(button.Name)));
        
        gamepad.ThumbstickMoved += (gp, thumbstick) => 
        {
            if (thumbstick.Index == 0)
            {
                Dispatch(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.LeftX, thumbstick.X));
                Dispatch(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.LeftY, thumbstick.Y));
            }
            else if (thumbstick.Index == 1)
            {
                Dispatch(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.RightX, thumbstick.X));
                Dispatch(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.RightY, thumbstick.Y));
            }
        };

        gamepad.TriggerMoved += (gp, trigger) =>
        {
            if (trigger.Index == 0)
            {
                Dispatch(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.LeftTrigger, trigger.Position));
            }
            else if (trigger.Index == 1)
            {
                Dispatch(new GamepadAxisMovedEvent(gp.Index, GamepadAxis.RightTrigger, trigger.Position));
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
                    mouse.Position = UnlockPosition();
                }

                global::Spot.Framework.Input.RelativeMouseMode = value;
            }
        }

        // Where the cursor reappears on unlock: back where the lock began, so it does not jump to the
        // centre mid-session. Falls back to the centre when that point is no longer inside the window — a
        // game that locks the cursor on startup saves whatever stale position the backend first reported,
        // which can sit off-window and leave the freed cursor (and the clicks meant for the dev console)
        // outside the game entirely.
        private System.Numerics.Vector2 UnlockPosition()
        {
            System.Numerics.Vector2 center = _windowCenter();
            System.Numerics.Vector2 size = center * 2.0f;
            bool inside = _unlockPosition.X > 0.0f && _unlockPosition.Y > 0.0f
                && _unlockPosition.X < size.X && _unlockPosition.Y < size.Y;
            return inside ? _unlockPosition : center;
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

            // Round to the nearest integer pixel so the snap position is always pixel-exact.
            // Without rounding, a window with an odd pixel dimension produces center.X = N.5f;
            // the OS snaps that to an integer and the next frame sees a constant 0.5px delta
            // that makes the camera drift even when the user is not moving the mouse.
            System.Numerics.Vector2 rawCenter = _windowCenter();
            System.Numerics.Vector2 center = new System.Numerics.Vector2(
                MathF.Round(rawCenter.X), MathF.Round(rawCenter.Y));

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
                global::Spot.Framework.Input.AddMouseMotion(delta);
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
