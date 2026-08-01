namespace Spot.Events;

/// <summary>
/// Raised when the window is requested to close.
/// </summary>
public sealed class WindowCloseEvent : Event
{
    /// <inheritdoc />
    public override EventType Type => EventType.WindowClose;

    /// <inheritdoc />
    public override string Name => "WindowClose";

    /// <inheritdoc />
    public override EventCategory CategoryFlags => EventCategory.Application;
}

/// <summary>
/// Raised when the window is resized.
/// </summary>
public sealed class WindowResizeEvent : Event
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowResizeEvent"/> class.
    /// </summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    public WindowResizeEvent(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the new width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the new height in pixels.
    /// </summary>
    public int Height { get; }

    /// <inheritdoc />
    public override EventType Type => EventType.WindowResize;

    /// <inheritdoc />
    public override string Name => "WindowResize";

    /// <inheritdoc />
    public override EventCategory CategoryFlags => EventCategory.Application;

    /// <inheritdoc />
    public override string ToString() => $"WindowResizeEvent: {Width}x{Height}";
}

/// <summary>
/// Raised when files are dropped onto the window.
/// </summary>
public sealed class WindowDropEvent : Event
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowDropEvent"/> class.
    /// </summary>
    /// <param name="paths">The dropped file paths.</param>
    public WindowDropEvent(string[] paths)
    {
        Paths = paths;
    }

    /// <summary>
    /// Gets the paths of the dropped files.
    /// </summary>
    public string[] Paths { get; }

    /// <inheritdoc />
    public override EventType Type => EventType.WindowDrop;

    /// <inheritdoc />
    public override string Name => "WindowDrop";

    /// <inheritdoc />
    public override EventCategory CategoryFlags => EventCategory.Application;

    /// <inheritdoc />
    public override string ToString() => $"WindowDropEvent: {Paths.Length} files";
}
