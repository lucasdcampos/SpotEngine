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
