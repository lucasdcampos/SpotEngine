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

    /// <summary>
    /// Gets the mouse position in window pixels, with the origin at the top-left.
    /// </summary>
    public static Vector2 MousePosition { get; private set; }

    /// <summary>
    /// Gets the mouse wheel movement accumulated during the current frame.
    /// </summary>
    public static Vector2 MouseScrollDelta { get; private set; }

    /// <summary>
    /// Returns whether the key is currently held down.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> while the key is down.</returns>
    public static bool GetKey(Key key) => DownKeys.Contains(key);

    /// <summary>
    /// Returns whether the key was pressed during this frame.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> on the frame the key goes down.</returns>
    public static bool GetKeyDown(Key key) => PressedThisFrame.Contains(key);

    /// <summary>
    /// Returns whether the key was released during this frame.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> on the frame the key goes up.</returns>
    public static bool GetKeyUp(Key key) => ReleasedThisFrame.Contains(key);

    /// <summary>
    /// Returns whether the mouse button is currently held down.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> while the button is down.</returns>
    public static bool GetMouseButton(MouseButton button) => DownButtons.Contains(button);

    /// <summary>
    /// Returns whether the mouse button was pressed during this frame.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> on the frame the button goes down.</returns>
    public static bool GetMouseButtonDown(MouseButton button) => ButtonsPressedThisFrame.Contains(button);

    /// <summary>
    /// Returns whether the mouse button was released during this frame.
    /// </summary>
    /// <param name="button">The button to test.</param>
    /// <returns><see langword="true"/> on the frame the button goes up.</returns>
    public static bool GetMouseButtonUp(MouseButton button) => ButtonsReleasedThisFrame.Contains(button);

    /// <summary>
    /// Clears the per-frame state. Called by the application before polling the next frame's events.
    /// </summary>
    internal static void NewFrame()
    {
        PressedThisFrame.Clear();
        ReleasedThisFrame.Clear();
        ButtonsPressedThisFrame.Clear();
        ButtonsReleasedThisFrame.Clear();
        MouseScrollDelta = Vector2.Zero;
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
                MousePosition = new Vector2(moved.X, moved.Y);
                break;

            case MouseScrolledEvent scrolled:
                MouseScrollDelta += new Vector2(scrolled.XOffset, scrolled.YOffset);
                break;
        }
    }
}
