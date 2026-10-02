namespace Spot.Core;

/// <summary>
/// Keyboard keys, independent of the windowing backend. Values match the underlying platform codes,
/// so <see cref="Input"/> and the key events can use them without exposing Silk.NET.
/// </summary>
public enum Key
{
    /// <summary>An unrecognized key.</summary>
    Unknown = -1,

    /// <summary>The spacebar.</summary>
    Space = 32,

    /// <summary>The apostrophe key (').</summary>
    Apostrophe = 39,

    /// <summary>The comma key (,).</summary>
    Comma = 44,

    /// <summary>The minus key (-).</summary>
    Minus = 45,

    /// <summary>The period key (.).</summary>
    Period = 46,

    /// <summary>The slash key (/).</summary>
    Slash = 47,

    /// <summary>The 0 key.</summary>
    Alpha0 = 48,

    /// <summary>The 1 key.</summary>
    Alpha1 = 49,

    /// <summary>The 2 key.</summary>
    Alpha2 = 50,

    /// <summary>The 3 key.</summary>
    Alpha3 = 51,

    /// <summary>The 4 key.</summary>
    Alpha4 = 52,

    /// <summary>The 5 key.</summary>
    Alpha5 = 53,

    /// <summary>The 6 key.</summary>
    Alpha6 = 54,

    /// <summary>The 7 key.</summary>
    Alpha7 = 55,

    /// <summary>The 8 key.</summary>
    Alpha8 = 56,

    /// <summary>The 9 key.</summary>
    Alpha9 = 57,

    /// <summary>The A key.</summary>
    A = 65,

    /// <summary>The B key.</summary>
    B = 66,

    /// <summary>The C key.</summary>
    C = 67,

    /// <summary>The D key.</summary>
    D = 68,

    /// <summary>The E key.</summary>
    E = 69,

    /// <summary>The F key.</summary>
    F = 70,

    /// <summary>The G key.</summary>
    G = 71,

    /// <summary>The H key.</summary>
    H = 72,

    /// <summary>The I key.</summary>
    I = 73,

    /// <summary>The J key.</summary>
    J = 74,

    /// <summary>The K key.</summary>
    K = 75,

    /// <summary>The L key.</summary>
    L = 76,

    /// <summary>The M key.</summary>
    M = 77,

    /// <summary>The N key.</summary>
    N = 78,

    /// <summary>The O key.</summary>
    O = 79,

    /// <summary>The P key.</summary>
    P = 80,

    /// <summary>The Q key.</summary>
    Q = 81,

    /// <summary>The R key.</summary>
    R = 82,

    /// <summary>The S key.</summary>
    S = 83,

    /// <summary>The T key.</summary>
    T = 84,

    /// <summary>The U key.</summary>
    U = 85,

    /// <summary>The V key.</summary>
    V = 86,

    /// <summary>The W key.</summary>
    W = 87,

    /// <summary>The X key.</summary>
    X = 88,

    /// <summary>The Y key.</summary>
    Y = 89,

    /// <summary>The Z key.</summary>
    Z = 90,

    /// <summary>The Escape key.</summary>
    Escape = 256,

    /// <summary>The Enter/Return key.</summary>
    Enter = 257,

    /// <summary>The Tab key.</summary>
    Tab = 258,

    /// <summary>The Backspace key.</summary>
    Backspace = 259,

    /// <summary>The Delete key.</summary>
    Delete = 261,

    /// <summary>The right arrow key.</summary>
    Right = 262,

    /// <summary>The left arrow key.</summary>
    Left = 263,

    /// <summary>The down arrow key.</summary>
    Down = 264,

    /// <summary>The up arrow key.</summary>
    Up = 265,

    /// <summary>The F1 key.</summary>
    F1 = 290,

    /// <summary>The F2 key.</summary>
    F2 = 291,

    /// <summary>The F3 key.</summary>
    F3 = 292,

    /// <summary>The F4 key.</summary>
    F4 = 293,

    /// <summary>The F5 key.</summary>
    F5 = 294,

    /// <summary>The F6 key.</summary>
    F6 = 295,

    /// <summary>The F7 key.</summary>
    F7 = 296,

    /// <summary>The F8 key.</summary>
    F8 = 297,

    /// <summary>The F9 key.</summary>
    F9 = 298,

    /// <summary>The F10 key.</summary>
    F10 = 299,

    /// <summary>The F11 key.</summary>
    F11 = 300,

    /// <summary>The F12 key.</summary>
    F12 = 301,

    /// <summary>The left Shift key.</summary>
    LeftShift = 340,

    /// <summary>The left Control key.</summary>
    LeftControl = 341,

    /// <summary>The left Alt key.</summary>
    LeftAlt = 342,

    /// <summary>The right Shift key.</summary>
    RightShift = 344,

    /// <summary>The right Control key.</summary>
    RightControl = 345,

    /// <summary>The right Alt key.</summary>
    RightAlt = 346,
}
