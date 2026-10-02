using System.Numerics;
using Spot.Framework.Events;

namespace Spot.Framework;

/// <summary>
/// Locks or unlocks the hardware cursor on behalf of <see cref="Input"/>, without coupling it to a
/// particular windowing backend. The active window installs an implementation — a Silk mouse on desktop,
/// the Pointer Lock API in the browser — so the same input code drives both. Implement it to plug in a
/// platform of your own.
/// </summary>
public interface ICursorController
{
    /// <summary>Gets or sets whether the hardware cursor is locked and hidden.</summary>
    bool Locked { get; set; }

    /// <summary>
    /// Per-frame upkeep while the cursor is locked. The desktop host recentres the OS cursor and reports
    /// the frame's relative motion through <see cref="Input.AddMouseMotion"/>, since the backend's own
    /// "confine" cursor modes proved unreliable (the cursor still escaped). Hosts that already deliver
    /// relative motion (the browser's Pointer Lock) leave this as a no-op.
    /// </summary>
    void Tick() { }
}

/// <summary>
/// Polled input state, queryable at any time: ask "is this key down?" instead of handling events. The
/// window feeds it (see <see cref="NewFrame"/> and <see cref="OnEvent"/>); for discrete, event-driven input,
/// handle the window's events directly.
/// </summary>
public static class Input
{
    /// <summary>
    /// Gets or sets the active platform's hardware-cursor controller, installed by the window once it exists.
    /// Null (and cursor operations no-op) in headless use and before a window is created.
    /// </summary>
    public static ICursorController? CursorController { get; set; }

    private static readonly HashSet<Key> DownKeys = new();
    private static readonly HashSet<Key> PressedThisFrame = new();
    private static readonly HashSet<Key> ReleasedThisFrame = new();

    private static readonly HashSet<MouseButton> DownButtons = new();
    private static readonly HashSet<MouseButton> ButtonsPressedThisFrame = new();
    private static readonly HashSet<MouseButton> ButtonsReleasedThisFrame = new();

    private static readonly HashSet<GamepadButton> DownGamepadButtons = new();
    private static readonly HashSet<GamepadButton> GamepadButtonsPressedThisFrame = new();
    private static readonly HashSet<GamepadButton> GamepadButtonsReleasedThisFrame = new();

    private static readonly Dictionary<(int, GamepadButton), bool> _gamepadButtonsByIndex = new();
    private static readonly Dictionary<(int, GamepadAxis), float> _gamepadAxes = new();
    private static readonly Dictionary<(int, GamepadAxis), float> _prevGamepadAxes = new();

    // What the app last asked for via CursorLocked. Kept separate from the hardware state so a capture
    // (e.g. a debug console) can override the cursor and later restore exactly what the app wanted.
    private static bool _desiredCursorLocked;

    // True while something above the app owns input (see Captured): the cursor is forced free/visible and
    // polled input is withheld.
    private static bool _engineCaptured;

    // True while reads are suppressed without touching the cursor (see Suppressed).
    private static bool _gameFocusCapture;

    // MousePosition is frozen at this value while input is blocked, so an app's frame-to-frame delta
    // tracking stays in sync: no camera spin while the cursor roams free, nor a jump when the block ends.
    private static Vector2 _frozenMousePosition;

    private static Vector2 _mousePosition;
    private static Vector2 _mouseScrollDelta;

    /// <summary>
    /// Gets or sets whether the mouse position is driven by accumulated relative motion
    /// (<see cref="AddMouseMotion"/>) instead of absolute move events. Set by the cursor controller while it
    /// locks the cursor for mouse-look.
    /// </summary>
    public static bool RelativeMouseMode { get; set; }

    /// <summary>
    /// Gets the mouse position in window pixels, with the origin at the top-left.
    /// </summary>
    /// <remarks>
    /// Returns the frozen position while input is blocked (<see cref="Captured"/> or <see cref="Suppressed"/>).
    /// Freezing is what keeps mouse-look still while, say, a debug console has the cursor: the hardware cursor
    /// is released and roams free, and a live position would feed that roaming straight into the camera as
    /// delta. The position is restored on release, so the first frame back sees a zero delta and no jump.
    /// </remarks>
    public static Vector2 MousePosition => InputBlocked ? _frozenMousePosition : _mousePosition;

    /// <summary>
    /// Gets the mouse wheel movement accumulated during the current frame.
    /// </summary>
    public static Vector2 MouseScrollDelta => InputBlocked ? Vector2.Zero : _mouseScrollDelta;

    /// <summary>
    /// Gets or sets whether the cursor is locked and hidden.
    /// </summary>
    /// <remarks>
    /// The getter reflects the real, effective hardware state: while input is captured the cursor is forced
    /// free, so this reads <see langword="false"/> even if the app asked for a locked cursor. Setting it records
    /// the request and applies it immediately unless input is blocked, in which case it is applied when the
    /// block ends.
    /// </remarks>
    public static bool CursorLocked
    {
        get => CursorController?.Locked ?? false;
        set
        {
            _desiredCursorLocked = value;
            // Don't apply the lock while input is blocked; RestoreCursor / releasing the capture applies it.
            if (!InputBlocked)
            {
                ApplyCursorMode(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets whether something above the app owns input — a debug console, an overlay, a pause menu
    /// drawn by another layer. While captured the cursor is forced free and visible, every query reports no
    /// input and <see cref="MousePosition"/> is frozen; releasing the capture restores the cursor state the
    /// app last requested. Idempotent.
    /// </summary>
    public static bool Captured
    {
        get => _engineCaptured;
        set
        {
            if (value == _engineCaptured)
            {
                return;
            }

            bool wasBlocked = InputBlocked;
            _engineCaptured = value;
            UpdateMouseFreeze(wasBlocked);
            ApplyCursorMode(value ? false : _desiredCursorLocked);
        }
    }

    /// <summary>
    /// Gets or sets whether input reads are suppressed without touching the cursor — for example while the
    /// app's view is not focused inside a larger tool. Idempotent; freezes <see cref="MousePosition"/> on the
    /// transition so delta tracking stays in sync.
    /// </summary>
    public static bool Suppressed
    {
        get => _gameFocusCapture;
        set
        {
            if (_gameFocusCapture == value) return;
            bool wasBlocked = InputBlocked;
            _gameFocusCapture = value;
            UpdateMouseFreeze(wasBlocked);
        }
    }

    /// <summary>
    /// Gets whether input is currently blocked (<see cref="Captured"/> or <see cref="Suppressed"/>), in which
    /// case every query reports no input.
    /// </summary>
    public static bool IsBlocked => InputBlocked;

    // Combined gate: any form of capture blocks all input reads.
    private static bool InputBlocked => _engineCaptured || _gameFocusCapture;

    // Freezes MousePosition when a block begins and restores it when the last block ends. Called after
    // either capture flag changes, with the blocked state as it was before the change.
    //
    // Freezing on entry keeps the app's delta tracking in sync: while blocked the cursor is free, so
    // _mousePosition keeps absorbing absolute OS move events that have nothing to do with mouse-look.
    // Restoring on exit makes the first unblocked delta zero — without it, _mousePosition holds a stale
    // absolute screen coordinate and the camera snaps.
    private static void UpdateMouseFreeze(bool wasBlocked)
    {
        bool blocked = InputBlocked;
        if (blocked == wasBlocked)
        {
            return;
        }

        if (blocked)
        {
            _frozenMousePosition = _mousePosition;
        }
        else
        {
            _mousePosition = _frozenMousePosition;
        }
    }

    /// <summary>
    /// Forces the hardware cursor free without altering the app's <see cref="CursorLocked"/> request
    /// (for example when the app's view loses focus). <see cref="RestoreCursor"/> re-applies the request.
    /// </summary>
    public static void ReleaseCursor() => ApplyCursorMode(false);

    /// <summary>
    /// Re-applies the app's last <see cref="CursorLocked"/> request.
    /// </summary>
    public static void RestoreCursor() => ApplyCursorMode(_desiredCursorLocked);

    // Writes the cursor mode through the active platform controller, a no-op when none is installed.
    private static void ApplyCursorMode(bool locked)
    {
        if (CursorController is { } controller)
        {
            controller.Locked = locked;
        }
    }

    /// <summary>
    /// Advances the platform cursor controller once per frame (see <see cref="ICursorController.Tick"/>).
    /// Called after polling input — <c>Window.PollEvents</c> does it — so a locked cursor is recentred and its
    /// motion applied before the app reads <see cref="MousePosition"/>.
    /// </summary>
    public static void TickCursorLock() => CursorController?.Tick();

    /// <summary>
    /// Accumulates relative mouse motion into <see cref="MousePosition"/>. Used by the host while the
    /// cursor is locked so frame-to-frame deltas keep driving mouse-look even though the hardware cursor
    /// is pinned in place.
    /// </summary>
    /// <param name="delta">The relative motion since the last frame, in pixels.</param>
    public static void AddMouseMotion(Vector2 delta)
    {
        // Skip accumulation while input is blocked: the cursor is unlocked and free, so there is no meaningful
        // locked-cursor motion to track. Keeping _mousePosition frozen keeps the app's delta tracking in sync.
        if (!InputBlocked) _mousePosition += delta;
    }

    /// <summary>
    /// Returns whether the key is currently held down.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> while the key is down.</returns>
    public static bool GetKey(Key key) => !InputBlocked && DownKeys.Contains(key);

    /// <summary>
    /// Returns whether the key was pressed during this frame.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> on the frame the key goes down.</returns>
    public static bool GetKeyDown(Key key) => !InputBlocked && PressedThisFrame.Contains(key);

    /// <summary>
    /// Returns whether the key was released during this frame.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> on the frame the key goes up.</returns>
    public static bool GetKeyUp(Key key) => !InputBlocked && ReleasedThisFrame.Contains(key);

    /// <summary>
    /// Returns whether the mouse button is currently held down.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> while the button is down.</returns>
    public static bool GetMouseButton(MouseButton button) => !InputBlocked && DownButtons.Contains(button);

    /// <summary>
    /// Returns whether the mouse button was pressed during this frame.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> on the frame the button goes down.</returns>
    public static bool GetMouseButtonDown(MouseButton button) => !InputBlocked && ButtonsPressedThisFrame.Contains(button);

    /// <summary>
    /// Returns whether the mouse button was released during this frame.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> on the frame the button goes up.</returns>
    public static bool GetMouseButtonUp(MouseButton button) => !InputBlocked && ButtonsReleasedThisFrame.Contains(button);

    /// <summary>
    /// Returns whether the gamepad button is currently held down on any connected gamepad.
    /// </summary>
    public static bool GetGamepadButton(GamepadButton button) => !InputBlocked && DownGamepadButtons.Contains(button);

    /// <summary>
    /// Returns whether the gamepad button was pressed during this frame on any connected gamepad.
    /// </summary>
    public static bool GetGamepadButtonDown(GamepadButton button) => !InputBlocked && GamepadButtonsPressedThisFrame.Contains(button);

    /// <summary>
    /// Returns whether the gamepad button was released during this frame on any connected gamepad.
    /// </summary>
    public static bool GetGamepadButtonUp(GamepadButton button) => !InputBlocked && GamepadButtonsReleasedThisFrame.Contains(button);

    /// <summary>
    /// Returns whether the gamepad button is currently held down on a specific gamepad.
    /// </summary>
    public static bool GetGamepadButton(int gamepadIndex, GamepadButton button) 
        => !InputBlocked && _gamepadButtonsByIndex.TryGetValue((gamepadIndex, button), out bool down) && down;

    /// <summary>
    /// Gets the current value of a gamepad axis for a specific gamepad. Returns 0 if disconnected or centered.
    /// </summary>
    public static float GetGamepadAxis(int gamepadIndex, GamepadAxis axis)
        => InputBlocked ? 0f : (_gamepadAxes.TryGetValue((gamepadIndex, axis), out float val) ? val : 0f);

    /// <summary>
    /// Gets an axis across every connected gamepad: the value furthest from center. Returns 0 while input is
    /// blocked or when no gamepad reports the axis.
    /// </summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The strongest value in [-1, 1] (triggers in [0, 1]).</returns>
    public static float GetGamepadAxis(GamepadAxis axis) => InputBlocked ? 0f : StrongestAxis(_gamepadAxes, axis);

    /// <summary>
    /// Gets <see cref="GetGamepadAxis(GamepadAxis)"/> as it was at the end of the previous frame, so an axis can be
    /// treated as a button (crossing a threshold this frame). Returns 0 while input is blocked.
    /// </summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The previous frame's strongest value.</returns>
    public static float GetPreviousGamepadAxis(GamepadAxis axis) =>
        InputBlocked ? 0f : StrongestAxis(_prevGamepadAxes, axis);

    private static float StrongestAxis(Dictionary<(int, GamepadAxis), float> axes, GamepadAxis axis)
    {
        float maxVal = 0f;
        foreach (var kvp in axes)
        {
            if (kvp.Key.Item2 == axis && Math.Abs(kvp.Value) > Math.Abs(maxVal))
            {
                maxVal = kvp.Value;
            }
        }
        return maxVal;
    }

    /// <summary>
    /// Raised by <see cref="NewFrame"/> after the per-frame state is cleared, so layers built on top (named
    /// actions, for example) can reset their own per-frame state in step.
    /// </summary>
    public static event Action? FrameStarted;

    /// <summary>
    /// Raised by <see cref="Reset"/> after all input state is cleared.
    /// </summary>
    public static event Action? Cleared;

    /// <summary>
    /// Starts a new input frame: clears the "this frame" presses/releases and the scroll delta. Call it once per
    /// frame before feeding the frame's events — <c>Window.PollEvents</c> does both for you.
    /// </summary>
    public static void NewFrame()
    {
        PressedThisFrame.Clear();
        ReleasedThisFrame.Clear();
        ButtonsPressedThisFrame.Clear();
        ButtonsReleasedThisFrame.Clear();
        GamepadButtonsPressedThisFrame.Clear();
        GamepadButtonsReleasedThisFrame.Clear();
        _mouseScrollDelta = Vector2.Zero;

        _prevGamepadAxes.Clear();
        foreach (var kvp in _gamepadAxes)
        {
            _prevGamepadAxes[kvp.Key] = kvp.Value;
        }

        FrameStarted?.Invoke();
    }

    /// <summary>
    /// Clears all input state — held keys and buttons, gamepads, mouse position, capture and suppression — as if
    /// no input had ever arrived. Useful when switching contexts and in tests.
    /// </summary>
    public static void Reset()
    {
        DownKeys.Clear();
        PressedThisFrame.Clear();
        ReleasedThisFrame.Clear();
        DownButtons.Clear();
        ButtonsPressedThisFrame.Clear();
        ButtonsReleasedThisFrame.Clear();
        DownGamepadButtons.Clear();
        GamepadButtonsPressedThisFrame.Clear();
        GamepadButtonsReleasedThisFrame.Clear();
        _gamepadButtonsByIndex.Clear();
        _gamepadAxes.Clear();
        _prevGamepadAxes.Clear();
        _mousePosition = Vector2.Zero;
        _mouseScrollDelta = Vector2.Zero;
        _frozenMousePosition = Vector2.Zero;
        _gameFocusCapture = false;
        _engineCaptured = false;
        RelativeMouseMode = false;
        Cleared?.Invoke();
    }

    /// <summary>
    /// Updates the input state from a window event. <c>Window.PollEvents</c> feeds every event through here;
    /// call it yourself to drive input from a platform of your own.
    /// </summary>
    /// <param name="e">The event.</param>
    public static void OnEvent(Event e)
    {
        switch (e)
        {
            case KeyPressedEvent pressed:
                // Guard against key-repeat so GetKeyDown is true for a single frame.
                if (DownKeys.Add(pressed.Key))
                {
                    PressedThisFrame.Add(pressed.Key);
                }

                break;

            case KeyReleasedEvent released:
                DownKeys.Remove(released.Key);
                ReleasedThisFrame.Add(released.Key);
                break;

            case MouseButtonPressedEvent buttonPressed:
                if (DownButtons.Add(buttonPressed.Button))
                {
                    ButtonsPressedThisFrame.Add(buttonPressed.Button);
                }

                break;

            case MouseButtonReleasedEvent buttonReleased:
                DownButtons.Remove(buttonReleased.Button);
                ButtonsReleasedThisFrame.Add(buttonReleased.Button);
                break;

            case MouseMovedEvent moved:
                // While the cursor is locked the host drives the position through AddMouseMotion, so
                // ignore absolute moves (which would otherwise snap it to the recentred hardware cursor).
                if (!RelativeMouseMode)
                {
                    _mousePosition = new Vector2(moved.X, moved.Y);
                }

                break;

            case MouseScrolledEvent scrolled:
                _mouseScrollDelta += new Vector2(scrolled.XOffset, scrolled.YOffset);
                break;

            case GamepadButtonPressedEvent gpPressed:
                if (DownGamepadButtons.Add(gpPressed.Button))
                {
                    GamepadButtonsPressedThisFrame.Add(gpPressed.Button);
                }
                _gamepadButtonsByIndex[(gpPressed.GamepadIndex, gpPressed.Button)] = true;
                break;

            case GamepadButtonReleasedEvent gpReleased:
                // Only consider it globally released if no other gamepad is holding it
                _gamepadButtonsByIndex[(gpReleased.GamepadIndex, gpReleased.Button)] = false;
                
                bool stillHeldAnywhere = false;
                foreach (var kvp in _gamepadButtonsByIndex)
                {
                    if (kvp.Key.Item2 == gpReleased.Button && kvp.Value)
                    {
                        stillHeldAnywhere = true;
                        break;
                    }
                }

                if (!stillHeldAnywhere)
                {
                    DownGamepadButtons.Remove(gpReleased.Button);
                    GamepadButtonsReleasedThisFrame.Add(gpReleased.Button);
                }
                break;

            case GamepadAxisMovedEvent gpAxis:
                _gamepadAxes[(gpAxis.GamepadIndex, gpAxis.Axis)] = gpAxis.Value;
                break;

            case GamepadDisconnectedEvent gpDconn:
                // Cleanup axes and buttons for this gamepad
                var keysToRemove = _gamepadAxes.Keys.Where(k => k.Item1 == gpDconn.GamepadIndex).ToList();
                foreach (var k in keysToRemove) _gamepadAxes.Remove(k);
                
                var btnKeysToRemove = _gamepadButtonsByIndex.Keys.Where(k => k.Item1 == gpDconn.GamepadIndex).ToList();
                foreach (var k in btnKeysToRemove)
                {
                    _gamepadButtonsByIndex.Remove(k);
                    // Re-evaluate global state
                    bool held = _gamepadButtonsByIndex.Any(kvp => kvp.Key.Item2 == k.Item2 && kvp.Value);
                    if (!held)
                    {
                        DownGamepadButtons.Remove(k.Item2);
                    }
                }
                break;
        }
    }
}
