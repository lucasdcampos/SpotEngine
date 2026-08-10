using System.Numerics;
using Spot.Events;

namespace Spot.Core;

/// <summary>
/// Polled input state, queryable at any time (typically from a scene's update). This is the
/// convenient, Unity-style path: ask "is this key down?" instead of handling events. For discrete,
/// event-driven input, override <see cref="Spot.Scenes.Scene.OnEvent"/> instead.
/// </summary>
public static class Input
{
    private static readonly HashSet<Key> DownKeys = new();
    private static readonly HashSet<Key> PressedThisFrame = new();
    private static readonly HashSet<Key> ReleasedThisFrame = new();

    private static readonly HashSet<MouseButton> DownButtons = new();
    private static readonly HashSet<MouseButton> ButtonsPressedThisFrame = new();
    private static readonly HashSet<MouseButton> ButtonsReleasedThisFrame = new();

    // What the game last asked for via CursorLocked. Kept separate from the hardware state so the
    // engine can override the cursor (e.g. while the dev console is open) and later restore exactly
    // what the game wanted.
    private static bool _desiredCursorLocked;

    // True while the engine owns input: the cursor is forced free/visible and polled input is
    // withheld from the game. Driven by Application from the console's open state.
    private static bool _engineCaptured;

    private static Vector2 _mousePosition;
    private static Vector2 _mouseScrollDelta;

    // Named actions mapped to the physical inputs that trigger them (an action can have several, e.g.
    // "forward" -> W and Up). Names are compared case-insensitively so console usage is forgiving.
    // _defaults holds the project's startup bindings so a runtime "resetbinds" can restore them.
    private static readonly Dictionary<string, HashSet<InputBinding>> _actions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<InputBinding>> _defaults = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the mouse position in window pixels, with the origin at the top-left.
    /// </summary>
    /// <remarks>
    /// Stays live even while the engine has captured input, so consumers that track a previous
    /// position (e.g. mouse-look) don't jump when capture ends.
    /// </remarks>
    public static Vector2 MousePosition => _mousePosition;

    /// <summary>
    /// Gets the mouse wheel movement accumulated during the current frame.
    /// </summary>
    public static Vector2 MouseScrollDelta => _engineCaptured ? Vector2.Zero : _mouseScrollDelta;

    /// <summary>
    /// Gets or sets whether the cursor is locked and hidden.
    /// </summary>
    /// <remarks>
    /// The getter reflects the real, effective hardware state: while the engine has captured input
    /// the cursor is forced free, so this reads <see langword="false"/> even if the game asked for a
    /// locked cursor. Setting it records the game's request and applies it immediately unless the
    /// engine is currently overriding the cursor, in which case it is applied when the override ends.
    /// </remarks>
    public static bool CursorLocked
    {
        get
        {
            var mice = Application.Instance.Window.Input.Mice;
            return mice.Count > 0 && mice[0].Cursor.CursorMode == Silk.NET.Input.CursorMode.Raw;
        }
        set
        {
            _desiredCursorLocked = value;
            if (!_engineCaptured)
            {
                ApplyCursorMode(value);
            }
        }
    }

    /// <summary>
    /// Gets whether the engine currently owns input (cursor forced free, game input withheld).
    /// </summary>
    internal static bool EngineCaptured => _engineCaptured;

    /// <summary>
    /// Sets whether the engine owns input. While captured the cursor is forced free/visible and the
    /// polled query methods report no input to the game; releasing capture restores the cursor state
    /// the game last requested. Idempotent.
    /// </summary>
    /// <param name="captured">Whether the engine should own input.</param>
    internal static void SetEngineCaptured(bool captured)
    {
        if (captured == _engineCaptured)
        {
            return;
        }

        _engineCaptured = captured;
        ApplyCursorMode(captured ? false : _desiredCursorLocked);
    }

    // Writes the cursor mode to the hardware, guarding against having no mouse device.
    private static void ApplyCursorMode(bool locked)
    {
        var mice = Application.Instance.Window.Input.Mice;
        if (mice.Count > 0)
        {
            mice[0].Cursor.CursorMode = locked ? Silk.NET.Input.CursorMode.Raw : Silk.NET.Input.CursorMode.Normal;
        }
    }

    /// <summary>
    /// Returns whether the key is currently held down.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> while the key is down.</returns>
    public static bool GetKey(Key key) => !_engineCaptured && DownKeys.Contains(key);

    /// <summary>
    /// Returns whether the key was pressed during this frame.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> on the frame the key goes down.</returns>
    public static bool GetKeyDown(Key key) => !_engineCaptured && PressedThisFrame.Contains(key);

    /// <summary>
    /// Returns whether the key was released during this frame.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> on the frame the key goes up.</returns>
    public static bool GetKeyUp(Key key) => !_engineCaptured && ReleasedThisFrame.Contains(key);

    /// <summary>
    /// Returns whether the mouse button is currently held down.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> while the button is down.</returns>
    public static bool GetMouseButton(MouseButton button) => !_engineCaptured && DownButtons.Contains(button);

    /// <summary>
    /// Returns whether the mouse button was pressed during this frame.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> on the frame the button goes down.</returns>
    public static bool GetMouseButtonDown(MouseButton button) => !_engineCaptured && ButtonsPressedThisFrame.Contains(button);

    /// <summary>
    /// Returns whether the mouse button was released during this frame.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> on the frame the button goes up.</returns>
    public static bool GetMouseButtonUp(MouseButton button) => !_engineCaptured && ButtonsReleasedThisFrame.Contains(button);

    /// <summary>
    /// Returns whether any input bound to the named action is currently held down.
    /// </summary>
    /// <param name="action">The action name (case-insensitive), e.g. "forward".</param>
    /// <returns><see langword="true"/> while any bound key/button is down.</returns>
    public static bool GetAction(string action)
    {
        if (_engineCaptured || !_actions.TryGetValue(action, out HashSet<InputBinding>? bindings))
        {
            return false;
        }

        foreach (InputBinding binding in bindings)
        {
            if (IsHeld(binding))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether the named action became active during this frame.
    /// </summary>
    /// <remarks>
    /// Fires only on the inactive-to-active transition: pressing a second bound key while the action is
    /// already held does not re-fire it.
    /// </remarks>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <returns><see langword="true"/> on the frame the action goes active.</returns>
    public static bool GetActionDown(string action)
    {
        if (_engineCaptured || !_actions.TryGetValue(action, out HashSet<InputBinding>? bindings))
        {
            return false;
        }

        bool pressedThisFrame = false;
        bool heldBefore = false;
        foreach (InputBinding binding in bindings)
        {
            bool pressed = IsPressed(binding);
            pressedThisFrame |= pressed;

            // Held coming into this frame (down now but not from this frame's press) means the action
            // was already active, so this isn't a fresh activation.
            heldBefore |= IsHeld(binding) && !pressed;
        }

        return pressedThisFrame && !heldBefore;
    }

    /// <summary>
    /// Returns whether the named action became inactive during this frame.
    /// </summary>
    /// <remarks>
    /// Fires only on the active-to-inactive transition: releasing one bound key while another is still
    /// held keeps the action active and does not fire.
    /// </remarks>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <returns><see langword="true"/> on the frame the action goes inactive.</returns>
    public static bool GetActionUp(string action)
    {
        if (_engineCaptured || !_actions.TryGetValue(action, out HashSet<InputBinding>? bindings))
        {
            return false;
        }

        bool releasedThisFrame = false;
        bool stillHeld = false;
        foreach (InputBinding binding in bindings)
        {
            releasedThisFrame |= IsReleased(binding);
            stillHeld |= IsHeld(binding);
        }

        return releasedThisFrame && !stillHeld;
    }

    /// <summary>
    /// Binds a physical input to an action. An action may have several bindings; binding one that is
    /// already present is a no-op.
    /// </summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <param name="binding">The key or mouse button to bind.</param>
    public static void Bind(string action, InputBinding binding)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        if (!_actions.TryGetValue(action, out HashSet<InputBinding>? bindings))
        {
            bindings = new HashSet<InputBinding>();
            _actions[action] = bindings;
        }

        bindings.Add(binding);
    }

    /// <summary>Binds a keyboard key to an action.</summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <param name="key">The key to bind.</param>
    public static void Bind(string action, Key key) => Bind(action, InputBinding.Key(key));

    /// <summary>Binds a mouse button to an action.</summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <param name="button">The button to bind.</param>
    public static void Bind(string action, MouseButton button) => Bind(action, InputBinding.Mouse(button));

    /// <summary>
    /// Removes a physical input from every action it is bound to.
    /// </summary>
    /// <param name="binding">The key or mouse button to unbind.</param>
    /// <returns><see langword="true"/> if the binding was removed from at least one action.</returns>
    public static bool Unbind(InputBinding binding)
    {
        bool removed = false;
        foreach (string action in _actions.Keys.ToList())
        {
            HashSet<InputBinding> bindings = _actions[action];
            if (bindings.Remove(binding))
            {
                removed = true;
                if (bindings.Count == 0)
                {
                    _actions.Remove(action);
                }
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes an action and all of its bindings.
    /// </summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <returns><see langword="true"/> if the action existed.</returns>
    public static bool UnbindAction(string action) => _actions.Remove(action);

    /// <summary>
    /// Gets the bindings currently mapped to an action.
    /// </summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <returns>A snapshot of the action's bindings, or an empty list if it has none.</returns>
    public static IReadOnlyList<InputBinding> GetBindings(string action)
        => _actions.TryGetValue(action, out HashSet<InputBinding>? bindings) ? bindings.ToArray() : Array.Empty<InputBinding>();

    /// <summary>
    /// Gets the names of all actions that currently have at least one binding.
    /// </summary>
    /// <returns>A snapshot of the action names.</returns>
    public static IReadOnlyList<string> GetActionNames() => _actions.Keys.ToArray();

    /// <summary>Removes every action binding.</summary>
    public static void ClearBindings() => _actions.Clear();

    /// <summary>
    /// Replaces the default bindings and applies them, discarding any current bindings. Called once at
    /// startup from the application spec so both the editor and shipped games get the project's
    /// defaults; the snapshot is what <see cref="ResetBindingsToDefaults"/> restores.
    /// </summary>
    /// <param name="defaults">Actions mapped to their default bindings. May be <see langword="null"/>.</param>
    public static void SetDefaultBindings(IReadOnlyDictionary<string, InputBinding[]>? defaults)
    {
        _defaults.Clear();
        if (defaults != null)
        {
            foreach ((string action, InputBinding[] bindings) in defaults)
            {
                if (string.IsNullOrWhiteSpace(action) || bindings == null)
                {
                    continue;
                }

                _defaults[action] = new HashSet<InputBinding>(bindings);
            }
        }

        ResetBindingsToDefaults();
    }

    /// <summary>
    /// Discards current bindings and restores the defaults set by <see cref="SetDefaultBindings"/>.
    /// </summary>
    public static void ResetBindingsToDefaults()
    {
        _actions.Clear();
        foreach ((string action, HashSet<InputBinding> bindings) in _defaults)
        {
            _actions[action] = new HashSet<InputBinding>(bindings);
        }
    }

    // Whether a single binding is held / went down / went up this frame, dispatching to the keyboard or
    // mouse frame state the action layer is built on.
    private static bool IsHeld(InputBinding binding) => binding.Device == InputDeviceKind.Keyboard
        ? DownKeys.Contains((Key)binding.Code)
        : DownButtons.Contains((MouseButton)binding.Code);

    private static bool IsPressed(InputBinding binding) => binding.Device == InputDeviceKind.Keyboard
        ? PressedThisFrame.Contains((Key)binding.Code)
        : ButtonsPressedThisFrame.Contains((MouseButton)binding.Code);

    private static bool IsReleased(InputBinding binding) => binding.Device == InputDeviceKind.Keyboard
        ? ReleasedThisFrame.Contains((Key)binding.Code)
        : ButtonsReleasedThisFrame.Contains((MouseButton)binding.Code);

    /// <summary>
    /// Clears the per-frame state. Called by the application before polling the next frame's events.
    /// </summary>
    internal static void NewFrame()
    {
        PressedThisFrame.Clear();
        ReleasedThisFrame.Clear();
        ButtonsPressedThisFrame.Clear();
        ButtonsReleasedThisFrame.Clear();
        _mouseScrollDelta = Vector2.Zero;
    }

    /// <summary>
    /// Resets all input state — held/frame keys and buttons, action bindings, defaults and capture —
    /// so headless tests start from a clean slate. Test-only.
    /// </summary>
    internal static void ResetForTests()
    {
        DownKeys.Clear();
        PressedThisFrame.Clear();
        ReleasedThisFrame.Clear();
        DownButtons.Clear();
        ButtonsPressedThisFrame.Clear();
        ButtonsReleasedThisFrame.Clear();
        _mousePosition = Vector2.Zero;
        _mouseScrollDelta = Vector2.Zero;
        _actions.Clear();
        _defaults.Clear();
        _engineCaptured = false;
    }

    /// <summary>
    /// Updates the input state from a window event. Called by the application for every event.
    /// </summary>
    /// <param name="e">The event.</param>
    internal static void OnEvent(Event e)
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
                _mousePosition = new Vector2(moved.X, moved.Y);
                break;

            case MouseScrolledEvent scrolled:
                _mouseScrollDelta += new Vector2(scrolled.XOffset, scrolled.YOffset);
                break;
        }
    }
}
