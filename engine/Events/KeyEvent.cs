namespace Spot.Events;

/// <summary>
/// Base class for keyboard events.
/// </summary>
public abstract class KeyEvent : Event
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyEvent"/> class.
    /// </summary>
    /// <param name="keyCode">The platform key code.</param>
    protected KeyEvent(int keyCode)
    {
        KeyCode = keyCode;
    }

    /// <summary>
    /// Gets the platform key code.
    /// </summary>
    public int KeyCode { get; }

    /// <inheritdoc />
    public override EventCategory CategoryFlags => EventCategory.Input | EventCategory.Keyboard;
}

/// <summary>
/// Raised when a key is pressed.
/// </summary>
public sealed class KeyPressedEvent : KeyEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyPressedEvent"/> class.
    /// </summary>
    /// <param name="keyCode">The platform key code.</param>
    /// <param name="repeatCount">The number of times the key press repeated.</param>
    public KeyPressedEvent(int keyCode, int repeatCount)
        : base(keyCode)
    {
        RepeatCount = repeatCount;
    }

    /// <summary>
    /// Gets the number of times the key press repeated.
    /// </summary>
    public int RepeatCount { get; }

    /// <inheritdoc />
    public override EventType Type => EventType.KeyPressed;

    /// <inheritdoc />
    public override string Name => "KeyPressed";

    /// <inheritdoc />
    public override string ToString() => $"KeyPressedEvent: {KeyCode} (repeat={RepeatCount})";
}

/// <summary>
/// Raised when a key is released.
/// </summary>
public sealed class KeyReleasedEvent : KeyEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyReleasedEvent"/> class.
    /// </summary>
    /// <param name="keyCode">The platform key code.</param>
    public KeyReleasedEvent(int keyCode)
        : base(keyCode)
    {
    }

    /// <inheritdoc />
    public override EventType Type => EventType.KeyReleased;

    /// <inheritdoc />
    public override string Name => "KeyReleased";

    /// <inheritdoc />
    public override string ToString() => $"KeyReleasedEvent: {KeyCode}";
}

/// <summary>
/// Raised when a character is typed.
/// </summary>
public sealed class KeyTypedEvent : KeyEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyTypedEvent"/> class.
    /// </summary>
    /// <param name="keyCode">The typed character code point.</param>
    public KeyTypedEvent(uint keyCode)
        : base((int)keyCode)
    {
    }

    /// <inheritdoc />
    public override EventType Type => EventType.KeyTyped;

    /// <inheritdoc />
    public override string Name => "KeyTyped";

    /// <inheritdoc />
    public override string ToString() => $"KeyTypedEvent: {KeyCode}";
}
