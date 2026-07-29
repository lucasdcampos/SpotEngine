namespace Spot.Events;

/// <summary>
/// Raised when the mouse moves.
/// </summary>
public sealed class MouseMovedEvent : Event
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseMovedEvent"/> class.
    /// </summary>
    /// <param name="x">The mouse X position.</param>
    /// <param name="y">The mouse Y position.</param>
    public MouseMovedEvent(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets the mouse X position.
    /// </summary>
    public float X { get; }

    /// <summary>
    /// Gets the mouse Y position.
    /// </summary>
    public float Y { get; }

    /// <inheritdoc />
    public override EventType Type => EventType.MouseMoved;

    /// <inheritdoc />
    public override string Name => "MouseMoved";

    /// <inheritdoc />
    public override EventCategory CategoryFlags => EventCategory.Input | EventCategory.Mouse;

    /// <inheritdoc />
    public override string ToString() => $"MouseMovedEvent: {X}, {Y}";
}

/// <summary>
/// Raised when the mouse wheel is scrolled.
/// </summary>
public sealed class MouseScrolledEvent : Event
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseScrolledEvent"/> class.
    /// </summary>
    /// <param name="xOffset">The horizontal scroll offset.</param>
    /// <param name="yOffset">The vertical scroll offset.</param>
    public MouseScrolledEvent(float xOffset, float yOffset)
    {
        XOffset = xOffset;
        YOffset = yOffset;
    }

    /// <summary>
    /// Gets the horizontal scroll offset.
    /// </summary>
    public float XOffset { get; }

    /// <summary>
    /// Gets the vertical scroll offset.
    /// </summary>
    public float YOffset { get; }

    /// <inheritdoc />
    public override EventType Type => EventType.MouseScrolled;

    /// <inheritdoc />
    public override string Name => "MouseScrolled";

    /// <inheritdoc />
    public override EventCategory CategoryFlags => EventCategory.Input | EventCategory.Mouse;

    /// <inheritdoc />
    public override string ToString() => $"MouseScrolledEvent: {XOffset}, {YOffset}";
}

/// <summary>
/// Base class for mouse button events.
/// </summary>
public abstract class MouseButtonEvent : Event
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseButtonEvent"/> class.
    /// </summary>
    /// <param name="button">The mouse button index.</param>
    protected MouseButtonEvent(int button)
    {
        Button = button;
    }

    /// <summary>
    /// Gets the mouse button index.
    /// </summary>
    public int Button { get; }

    /// <inheritdoc />
    public override EventCategory CategoryFlags =>
        EventCategory.Input | EventCategory.Mouse | EventCategory.MouseButton;
}

/// <summary>
/// Raised when a mouse button is pressed.
/// </summary>
public sealed class MouseButtonPressedEvent : MouseButtonEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseButtonPressedEvent"/> class.
    /// </summary>
    /// <param name="button">The mouse button index.</param>
    public MouseButtonPressedEvent(int button)
        : base(button)
    {
    }

    /// <inheritdoc />
    public override EventType Type => EventType.MouseButtonPressed;

    /// <inheritdoc />
    public override string Name => "MouseButtonPressed";

    /// <inheritdoc />
    public override string ToString() => $"MouseButtonPressedEvent: {Button}";
}

/// <summary>
/// Raised when a mouse button is released.
/// </summary>
public sealed class MouseButtonReleasedEvent : MouseButtonEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MouseButtonReleasedEvent"/> class.
    /// </summary>
    /// <param name="button">The mouse button index.</param>
    public MouseButtonReleasedEvent(int button)
        : base(button)
    {
    }

    /// <inheritdoc />
    public override EventType Type => EventType.MouseButtonReleased;

    /// <inheritdoc />
    public override string Name => "MouseButtonReleased";

    /// <inheritdoc />
    public override string ToString() => $"MouseButtonReleasedEvent: {Button}";
}
