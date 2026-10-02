namespace Spot.Core;

/// <summary>
/// Mouse buttons, independent of the windowing backend. Values match the underlying platform codes.
/// </summary>
public enum MouseButton
{
    /// <summary>An unrecognized button.</summary>
    Unknown = -1,

    /// <summary>The left mouse button.</summary>
    Left = 0,

    /// <summary>The right mouse button.</summary>
    Right = 1,

    /// <summary>The middle mouse button (wheel click).</summary>
    Middle = 2,

    /// <summary>The fourth mouse button.</summary>
    Button4 = 3,

    /// <summary>The fifth mouse button.</summary>
    Button5 = 4,
}
