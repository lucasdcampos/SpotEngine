using Spot.Core;

namespace Spot.Events;

/// <summary>
/// Base class for keyboard key events.
/// </summary>
public abstract class KeyEvent : Event
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyEvent"/> class.
    /// </summary>
    /// <param name="key">The key.</param>
    protected KeyEvent(Key key)
    {
        Key = key;
    }

    /// <summary>
    /// Gets the key.
    /// </summary>
    public Key Key { get; }

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
    /// <param name="key">The key.</param>
    public KeyPressedEvent(Key key)
        : base(key)
    {
    }

    /// <inheritdoc />
    public override EventType Type => EventType.KeyPressed;

    /// <inheritdoc />
    public override string Name => "KeyPressed";

    /// <inheritdoc />
    public override string ToString() => $"KeyPressedEvent: {Key}";
}

/// <summary>
/// Raised when a key is released.
/// </summary>
public sealed class KeyReleasedEvent : KeyEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyReleasedEvent"/> class.
    /// </summary>
    /// <param name="key">The key.</param>
    public KeyReleasedEvent(Key key)
        : base(key)
    {
    }

    /// <inheritdoc />
    public override EventType Type => EventType.KeyReleased;

    /// <inheritdoc />
    public override string Name => "KeyReleased";

    /// <inheritdoc />
    public override string ToString() => $"KeyReleasedEvent: {Key}";
}

/// <summary>
/// Raised when a character is typed (after layout and modifiers are applied). Use this for text
/// input; use <see cref="KeyPressedEvent"/> for physical keys.
/// </summary>
public sealed class KeyTypedEvent : Event
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KeyTypedEvent"/> class.
    /// </summary>
    /// <param name="character">The typed character.</param>
    public KeyTypedEvent(char character)
    {
        Character = character;
    }

    /// <summary>
    /// Gets the typed character.
    /// </summary>
    public char Character { get; }

    /// <inheritdoc />
    public override EventType Type => EventType.KeyTyped;

    /// <inheritdoc />
    public override string Name => "KeyTyped";

    /// <inheritdoc />
    public override EventCategory CategoryFlags => EventCategory.Input | EventCategory.Keyboard;

    /// <inheritdoc />
    public override string ToString() => $"KeyTypedEvent: {Character}";
}
