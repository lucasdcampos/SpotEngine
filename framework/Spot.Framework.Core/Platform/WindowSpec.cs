namespace Spot.Framework;

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

    /// <summary>
    /// Gets or sets whether presentation waits for vertical sync. Defaults to on.
    /// </summary>
    public bool VSync { get; set; } = true;

    /// <summary>
    /// Gets or sets the window icon, as raw pixels (see <see cref="WindowIcon"/>). Null keeps the platform default.
    /// </summary>
    public WindowIcon? Icon { get; set; }
}

/// <summary>
/// A window icon as raw RGBA8 pixels, rows top-to-bottom.
/// </summary>
/// <param name="Width">The icon width in pixels.</param>
/// <param name="Height">The icon height in pixels.</param>
/// <param name="Rgba">The pixel data, <c>Width * Height * 4</c> bytes.</param>
public sealed record WindowIcon(int Width, int Height, byte[] Rgba);
