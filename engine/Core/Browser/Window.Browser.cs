using Spot.Engine.Browser;
using Spot.Engine.Events;
using Spot.Engine.Graphics;

namespace Spot.Engine;

/// <summary>
/// The browser's window: the page's canvas with a WebGL2 context. The same public surface as the desktop window,
/// so code written against it runs on both targets. Creating one installs the WebGL2 device as the
/// <see cref="Renderer"/>'s device and routes the page's input through <see cref="BrowserPlatform"/> into
/// <see cref="Input"/>. The page owns the frame clock: drive your frame from
/// <see cref="BrowserPlatform.AnimationFrame"/>, calling <see cref="PollEvents"/> first.
/// </summary>
public sealed class Window : IDisposable
{
    private EventCallback? _callback;
    private bool _closing;

    /// <summary>
    /// Initializes the window over the page's canvas.
    /// </summary>
    /// <param name="spec">The window specification. The size is the canvas' drawable size, in pixels.</param>
    public Window(WindowSpec spec)
    {
        Title = spec.Title;
        Mode = spec.Mode;
        (int canvasWidth, int canvasHeight) = BrowserPlatform.CanvasSize;
        Width = canvasWidth > 0 ? canvasWidth : spec.Width;
        Height = canvasHeight > 0 ? canvasHeight : spec.Height;
        Display.SetSize(Width, Height);

        Renderer.Init(new WebGL2GraphicsDevice());
        Spot.Engine.Input.CursorController = new BrowserCursorController();
        BrowserPlatform.Current = this;
        SyncViewport();
    }

    /// <summary>Gets the canvas width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the canvas height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the drawable width in pixels (the canvas is sized in device pixels).</summary>
    public int FramebufferWidth => Width;

    /// <summary>Gets the drawable height in pixels.</summary>
    public int FramebufferHeight => Height;

    /// <summary>Gets or sets the window title. Kept for parity with the desktop window; the page owns its title.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets VSync. Always on in the browser — frames are paced by <c>requestAnimationFrame</c>.</summary>
    public bool VSync
    {
        get => true;
        set { }
    }

    /// <summary>
    /// Gets or sets the window mode. Kept for parity with the desktop window: the page owns the canvas' size, and
    /// browsers only grant fullscreen from a user gesture, so the value is recorded but has no effect.
    /// </summary>
    public WindowMode Mode { get; set; }

    /// <summary>Gets whether the window is open (until <see cref="Close"/>).</summary>
    public bool IsOpen => !_closing;

    /// <summary>Sets the callback invoked for every event, after <see cref="Input"/> has seen it.</summary>
    /// <param name="callback">The event callback.</param>
    public void SetEventCallback(EventCallback callback) => _callback = callback;

    /// <summary>
    /// Starts a new <see cref="Input"/> frame and delivers the input queued since the last frame to
    /// <see cref="Input"/> and then to the event callback.
    /// </summary>
    public void PollEvents()
    {
        Spot.Engine.Input.NewFrame();
        foreach (Event e in BrowserPlatform.DrainPending())
        {
            Dispatch(e);
        }

        Spot.Engine.Input.TickCursorLock();

        if (Renderer.ViewportWidth == 0 || Renderer.ViewportHeight == 0)
        {
            SyncViewport();
        }
    }

    /// <summary>Presents the frame. A no-op: the browser presents when the animation frame returns.</summary>
    public void SwapBuffers()
    {
    }

    /// <summary>Gets whether a close was requested.</summary>
    /// <returns><see langword="true"/> after <see cref="Close"/>.</returns>
    public bool ShouldClose() => _closing;

    /// <summary>Cancels a pending close request.</summary>
    public void CancelClose() => _closing = false;

    /// <summary>Requests the window to close; <see cref="IsOpen"/> becomes <see langword="false"/>.</summary>
    public void Close() => _closing = true;

    /// <summary>Sets the window icon. A no-op in the browser (the page's favicon applies).</summary>
    /// <param name="icon">The icon pixels.</param>
    public void SetIcon(WindowIcon icon)
    {
    }

    /// <summary>Points the renderer's viewport at the whole canvas.</summary>
    public void SyncViewport() =>
        Renderer.SetViewport(0, 0, (uint)Math.Max(1, Width), (uint)Math.Max(1, Height));

    /// <inheritdoc />
    public void Dispose()
    {
        if (ReferenceEquals(BrowserPlatform.Current, this))
        {
            BrowserPlatform.Current = null;
        }
    }

    /// <summary>Delivers an event to <see cref="Input"/> and then the callback. Never throws.</summary>
    /// <param name="e">The event.</param>
    internal void Dispatch(Event e)
    {
        try
        {
            Spot.Engine.Input.OnEvent(e);
            _callback?.Invoke(e);
        }
        catch (Exception ex)
        {
            Log.CoreError("Browser event handling error: {0}", ex);
        }
    }

    /// <summary>Applies a canvas resize reported by the page.</summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    internal void OnCanvasResize(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Display.SetSize(Width, Height);
        SyncViewport();
        Dispatch(new WindowResizeEvent(Width, Height));
    }
}
