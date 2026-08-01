namespace Spot.Events;

/// <summary>
/// Identifies the concrete type of an <see cref="Event"/>.
/// </summary>
public enum EventType
{
    None = 0,
    WindowClose,
    WindowResize,
    WindowFocus,
    WindowLostFocus,
    WindowMoved,
    WindowDrop,
    AppTick,
    AppUpdate,
    AppRender,
    KeyPressed,
    KeyReleased,
    KeyTyped,
    MouseButtonPressed,
    MouseButtonReleased,
    MouseMoved,
    MouseScrolled,
}

/// <summary>
/// Broad categories an <see cref="Event"/> can belong to.
/// </summary>
[Flags]
public enum EventCategory
{
    None = 0,
    Application = 1 << 0,
    Input = 1 << 1,
    Keyboard = 1 << 2,
    Mouse = 1 << 3,
    MouseButton = 1 << 4,
}

/// <summary>
/// Base class for all events raised by the engine.
/// </summary>
public abstract class Event
{
    /// <summary>
    /// Gets or sets a value indicating whether the event has been handled.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Gets the concrete type of this event.
    /// </summary>
    public abstract EventType Type { get; }

    /// <summary>
    /// Gets the human-readable name of this event.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the categories this event belongs to.
    /// </summary>
    public abstract EventCategory CategoryFlags { get; }

    /// <summary>
    /// Determines whether this event is part of the specified category.
    /// </summary>
    /// <param name="category">The category to test.</param>
    /// <returns><see langword="true"/> if the event is in the category; otherwise, <see langword="false"/>.</returns>
    public bool IsInCategory(EventCategory category) => (CategoryFlags & category) != 0;

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// Handles an <see cref="Event"/>.
/// </summary>
/// <param name="e">The event to handle.</param>
public delegate void EventCallback(Event e);

/// <summary>
/// Dispatches an event to a strongly typed handler when the runtime type matches.
/// </summary>
public sealed class EventDispatcher
{
    private readonly Event _event;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventDispatcher"/> class.
    /// </summary>
    /// <param name="e">The event to dispatch.</param>
    public EventDispatcher(Event e)
    {
        _event = e;
    }

    /// <summary>
    /// Dispatches the event to <paramref name="func"/> when it is of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The event type to match.</typeparam>
    /// <param name="func">The handler that returns whether the event was handled.</param>
    /// <returns><see langword="true"/> if the event matched and was dispatched; otherwise, <see langword="false"/>.</returns>
    public bool Dispatch<T>(Func<T, bool> func)
        where T : Event
    {
        if (_event is T typed)
        {
            _event.Handled |= func(typed);
            return true;
        }

        return false;
    }
}
