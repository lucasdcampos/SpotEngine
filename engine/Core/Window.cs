using Silk.NET.Maths;
using Silk.NET.Windowing;
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
}

/// <summary>
/// A platform window backed by Silk.NET.
/// </summary>
public sealed class Window : IDisposable
{
    private readonly WindowSpec _spec;
    private readonly IWindow _window;

    /// <summary>
    /// Initializes a new instance of the <see cref="Window"/> class.
    /// </summary>
    /// <param name="spec">The window specification.</param>
    public Window(WindowSpec spec)
    {
        _spec = spec;

        WindowOptions options = WindowOptions.Default;
        options.Title = spec.Title;
        options.Size = new Vector2D<int>(spec.Width, spec.Height);
        options.API = GraphicsAPI.None;
        options.WindowBorder = WindowBorder.Resizable;

        _window = SilkWindow.Create(options);
        _window.Initialize();

        Log.CoreInfo("Window '{0}' created ({1}x{2})", spec.Title, spec.Width, spec.Height);
    }

    /// <summary>
    /// Gets the window width in pixels.
    /// </summary>
    public int Width => _spec.Width;

    /// <summary>
    /// Gets the window height in pixels.
    /// </summary>
    public int Height => _spec.Height;

    /// <summary>
    /// Gets the window title.
    /// </summary>
    public string Title => _spec.Title;

    /// <summary>
    /// Gets the underlying Silk.NET window.
    /// </summary>
    public IWindow NativeWindow => _window;

    /// <summary>
    /// Processes pending window and input events.
    /// </summary>
    public void PollEvents() => _window.DoEvents();

    /// <summary>
    /// Gets a value indicating whether the window has been requested to close.
    /// </summary>
    /// <returns><see langword="true"/> if the window should close; otherwise, <see langword="false"/>.</returns>
    public bool ShouldClose() => _window.IsClosing;

    /// <inheritdoc />
    public void Dispose()
    {
        _window.DoEvents();
        _window.Reset();
        _window.Dispose();
    }
}
