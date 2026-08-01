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

        WindowOptions options = WindowOptions.Default;
        options.Title = spec.Title;
        options.Size = new Vector2D<int>(spec.Width, spec.Height);
        options.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.Default,
            new APIVersion(4, 6));
        options.WindowBorder = WindowBorder.Resizable;
        options.VSync = true;

        _window = SilkWindow.Create(options);
        _window.Initialize();

        _input = _window.CreateInput();
        SetupCallbacks();

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
    /// Gets the window title.
    /// </summary>
    public string Title => _spec.Title;

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
        _input.Dispose();
        _window.DoEvents();
        _window.Reset();
        _window.Dispose();
    }

    private void SetupCallbacks()
    {
        _window.Closing += () => _callback?.Invoke(new WindowCloseEvent());

        _window.Resize += size =>
        {
            _width = size.X;
            _height = size.Y;
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
    }
}
