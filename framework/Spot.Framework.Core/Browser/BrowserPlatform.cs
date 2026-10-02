using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using Spot.Framework;
using Spot.Framework.Events;

namespace Spot.Framework.Browser;

/// <summary>
/// The browser's platform entry points: the page's JavaScript calls these (<c>[JSExport]</c>) from
/// <c>requestAnimationFrame</c>, canvas resizes and DOM input events. Input is queued for the browser
/// <see cref="Window"/> to deliver on its next <see cref="Window.PollEvents"/>; frames are raised as
/// <see cref="AnimationFrame"/>, which is how a browser app drives its loop — the page owns the frame clock, so
/// there is no blocking <c>while</c> loop on this target.
/// </summary>
public static partial class BrowserPlatform
{
    // DOM events arrive between frames, after the previous Input.NewFrame already ran. Delivering them at once
    // would let the next NewFrame clear their pressed/released edges before anything reads them, so queue them
    // and let Window.PollEvents flush them right after it starts the new input frame.
    private static readonly List<Event> s_pending = new();

    // The virtual cursor position, advanced by relative motion while the pointer is locked so mouse-look deltas
    // keep flowing (the browser freezes absolute coordinates under pointer lock).
    private static Vector2 s_mousePosition;

    /// <summary>
    /// Raised once per <c>requestAnimationFrame</c> with the seconds since the previous frame. Subscribe to run
    /// your frame: poll the window, update, draw. A throwing handler is logged and the next frame still runs.
    /// </summary>
    public static event Action<double>? AnimationFrame;

    /// <summary>Gets the last canvas size reported by the page (zero until the first resize).</summary>
    public static (int Width, int Height) CanvasSize { get; private set; }

    /// <summary>Gets or sets the window that receives input, if one has been created.</summary>
    internal static Window? Current { get; set; }

    /// <summary>Moves the queued input events out, in arrival order.</summary>
    /// <returns>The events queued since the last call.</returns>
    internal static Event[] DrainPending()
    {
        Event[] events = s_pending.ToArray();
        s_pending.Clear();
        return events;
    }

    /// <summary>Runs one frame. Called by the page from <c>requestAnimationFrame</c>.</summary>
    /// <param name="deltaTime">The seconds since the previous frame.</param>
    [JSExport]
    internal static void Frame(double deltaTime)
    {
        try
        {
            AnimationFrame?.Invoke(deltaTime);
        }
        catch (Exception ex)
        {
            Log.CoreError("Browser frame error: {0}", ex);
        }
    }

    /// <summary>Handles a canvas resize from the page.</summary>
    /// <param name="width">The new drawable width in pixels.</param>
    /// <param name="height">The new drawable height in pixels.</param>
    [JSExport]
    internal static void Resize(int width, int height)
    {
        CanvasSize = (width, height);
        Current?.OnCanvasResize(width, height);
    }

    /// <summary>A DOM keydown. <paramref name="code"/> is the <c>KeyboardEvent.code</c> value.</summary>
    /// <param name="code">The physical key code.</param>
    [JSExport]
    internal static void KeyDown(string code)
    {
        Key key = DomInput.MapKey(code);
        if (key != Key.Unknown)
        {
            s_pending.Add(new KeyPressedEvent(key));
        }
    }

    /// <summary>A DOM keyup. <paramref name="code"/> is the <c>KeyboardEvent.code</c> value.</summary>
    /// <param name="code">The physical key code.</param>
    [JSExport]
    internal static void KeyUp(string code)
    {
        Key key = DomInput.MapKey(code);
        if (key != Key.Unknown)
        {
            s_pending.Add(new KeyReleasedEvent(key));
        }
    }

    /// <summary>Typed text (a <c>keypress</c>/composition result), one event per character.</summary>
    /// <param name="text">The typed text.</param>
    [JSExport]
    internal static void TextInput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (char c in text)
        {
            s_pending.Add(new KeyTypedEvent(c));
        }
    }

    /// <summary>An absolute pointer move, in device pixels from the canvas' top-left.</summary>
    /// <param name="x">The pointer x position.</param>
    /// <param name="y">The pointer y position.</param>
    [JSExport]
    internal static void PointerMove(double x, double y)
    {
        s_mousePosition = new Vector2((float)x, (float)y);
        DeliverNow(new MouseMovedEvent(s_mousePosition.X, s_mousePosition.Y));
    }

    /// <summary>A relative pointer move (device pixels) while the pointer is locked.</summary>
    /// <param name="dx">The horizontal movement.</param>
    /// <param name="dy">The vertical movement.</param>
    [JSExport]
    internal static void PointerMoveRelative(double dx, double dy)
    {
        s_mousePosition += new Vector2((float)dx, (float)dy);
        DeliverNow(new MouseMovedEvent(s_mousePosition.X, s_mousePosition.Y));
    }

    /// <summary>A pointer button press. <paramref name="button"/> is the DOM <c>MouseEvent.button</c>.</summary>
    /// <param name="button">The DOM button index.</param>
    [JSExport]
    internal static void PointerDown(int button) => s_pending.Add(new MouseButtonPressedEvent(DomInput.MapButton(button)));

    /// <summary>A pointer button release. <paramref name="button"/> is the DOM <c>MouseEvent.button</c>.</summary>
    /// <param name="button">The DOM button index.</param>
    [JSExport]
    internal static void PointerUp(int button) => s_pending.Add(new MouseButtonReleasedEvent(DomInput.MapButton(button)));

    /// <summary>A mouse wheel scroll.</summary>
    /// <param name="deltaX">The horizontal scroll amount.</param>
    /// <param name="deltaY">The vertical scroll amount.</param>
    [JSExport]
    internal static void Wheel(double deltaX, double deltaY) =>
        s_pending.Add(new MouseScrolledEvent((float)deltaX, (float)deltaY));

    // The mouse position is a polled value, not a one-frame edge, so moves are delivered at once (pixel-accurate
    // hover). Before a window exists they wait in the queue like everything else.
    private static void DeliverNow(Event e)
    {
        if (Current is { } window)
        {
            window.Dispatch(e);
        }
        else
        {
            s_pending.Add(e);
        }
    }
}
