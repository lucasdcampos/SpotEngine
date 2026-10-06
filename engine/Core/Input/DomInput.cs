namespace Spot.Engine;

/// <summary>
/// Maps browser DOM input codes to the engine's backend-neutral <see cref="Key"/> and <see cref="MouseButton"/>.
/// Pure logic, compiled for every target so it can be tested off the browser.
/// </summary>
public static class DomInput
{
    /// <summary>
    /// Maps a DOM <c>KeyboardEvent.code</c> (e.g. <c>KeyW</c>, <c>Digit1</c>, <c>ArrowUp</c>, <c>F5</c>) to a
    /// <see cref="Key"/>. Letters, digits and function keys are computed; the rest come from a table.
    /// </summary>
    /// <param name="code">The physical key code.</param>
    /// <returns>The key, or <see cref="Key.Unknown"/> for an unrecognized code.</returns>
    public static Key MapKey(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return Key.Unknown;
        }

        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal))
        {
            char c = code[3];
            if (c is >= 'A' and <= 'Z')
            {
                return Key.A + (c - 'A');
            }
        }

        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal))
        {
            char d = code[5];
            if (d is >= '0' and <= '9')
            {
                return Key.Alpha0 + (d - '0');
            }
        }

        if (code.Length is 2 or 3 && code[0] == 'F' && int.TryParse(code.AsSpan(1), out int fn) && fn is >= 1 and <= 12)
        {
            return Key.F1 + (fn - 1);
        }

        return code switch
        {
            "Space" => Key.Space,
            "Enter" or "NumpadEnter" => Key.Enter,
            "Escape" => Key.Escape,
            "Tab" => Key.Tab,
            "Backspace" => Key.Backspace,
            "Delete" => Key.Delete,
            "ArrowRight" => Key.Right,
            "ArrowLeft" => Key.Left,
            "ArrowDown" => Key.Down,
            "ArrowUp" => Key.Up,
            "ShiftLeft" => Key.LeftShift,
            "ShiftRight" => Key.RightShift,
            "ControlLeft" => Key.LeftControl,
            "ControlRight" => Key.RightControl,
            "AltLeft" => Key.LeftAlt,
            "AltRight" => Key.RightAlt,
            "Comma" => Key.Comma,
            "Period" => Key.Period,
            "Slash" => Key.Slash,
            "Minus" => Key.Minus,
            "Quote" => Key.Apostrophe,
            _ => Key.Unknown,
        };
    }

    /// <summary>
    /// Maps a DOM <c>MouseEvent.button</c> index to a <see cref="MouseButton"/>.
    /// </summary>
    /// <param name="domButton">The DOM button index (0 left, 1 middle, 2 right, 3/4 back/forward).</param>
    /// <returns>The button, or <see cref="MouseButton.Unknown"/>.</returns>
    public static MouseButton MapButton(int domButton) => domButton switch
    {
        0 => MouseButton.Left,
        1 => MouseButton.Middle,
        2 => MouseButton.Right,
        3 => MouseButton.Button4,
        4 => MouseButton.Button5,
        _ => MouseButton.Unknown,
    };
}
