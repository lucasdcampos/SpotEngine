using Spot.Core;

namespace Spot.Events;

/// <summary>
/// Fired when a gamepad button goes down.
/// </summary>
public class GamepadButtonPressedEvent : Event
{
    public int GamepadIndex { get; }
    public GamepadButton Button { get; }

    public GamepadButtonPressedEvent(int gamepadIndex, GamepadButton button)
    {
        GamepadIndex = gamepadIndex;
        Button = button;
    }

    public override EventType Type => EventType.GamepadButtonPressed;
    public override string Name => "GamepadButtonPressed";
    public override EventCategory CategoryFlags => EventCategory.Input | EventCategory.Gamepad;
}

/// <summary>
/// Fired when a gamepad button goes up.
/// </summary>
public class GamepadButtonReleasedEvent : Event
{
    public int GamepadIndex { get; }
    public GamepadButton Button { get; }

    public GamepadButtonReleasedEvent(int gamepadIndex, GamepadButton button)
    {
        GamepadIndex = gamepadIndex;
        Button = button;
    }

    public override EventType Type => EventType.GamepadButtonReleased;
    public override string Name => "GamepadButtonReleased";
    public override EventCategory CategoryFlags => EventCategory.Input | EventCategory.Gamepad;
}

/// <summary>
/// Fired when a gamepad axis moves.
/// </summary>
public class GamepadAxisMovedEvent : Event
{
    public int GamepadIndex { get; }
    public GamepadAxis Axis { get; }
    public float Value { get; }

    public GamepadAxisMovedEvent(int gamepadIndex, GamepadAxis axis, float value)
    {
        GamepadIndex = gamepadIndex;
        Axis = axis;
        Value = value;
    }

    public override EventType Type => EventType.GamepadAxisMoved;
    public override string Name => "GamepadAxisMoved";
    public override EventCategory CategoryFlags => EventCategory.Input | EventCategory.Gamepad;
}

/// <summary>
/// Fired when a gamepad connects.
/// </summary>
public class GamepadConnectedEvent : Event
{
    public int GamepadIndex { get; }

    public GamepadConnectedEvent(int gamepadIndex)
    {
        GamepadIndex = gamepadIndex;
    }

    public override EventType Type => EventType.GamepadConnected;
    public override string Name => "GamepadConnected";
    public override EventCategory CategoryFlags => EventCategory.Input | EventCategory.Gamepad;
}

/// <summary>
/// Fired when a gamepad disconnects.
/// </summary>
public class GamepadDisconnectedEvent : Event
{
    public int GamepadIndex { get; }

    public GamepadDisconnectedEvent(int gamepadIndex)
    {
        GamepadIndex = gamepadIndex;
    }

    public override EventType Type => EventType.GamepadDisconnected;
    public override string Name => "GamepadDisconnected";
    public override EventCategory CategoryFlags => EventCategory.Input | EventCategory.Gamepad;
}

