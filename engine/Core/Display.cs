namespace Spot.Core;

/// <summary>
/// The current render surface size in pixels — the desktop window's client area, or the browser canvas.
/// It is a backend-neutral seam so engine code (camera viewport sizing, UI layout) can read the drawable
/// dimensions without depending on the desktop <see cref="Window"/>. The active host keeps it current: the
/// desktop window updates it on creation and resize; the browser host updates it from the canvas.
/// </summary>
public static class Display
{
    /// <summary>Gets the render surface width in pixels (never less than 1).</summary>
    public static int Width { get; private set; } = 1;

    /// <summary>Gets the render surface height in pixels (never less than 1).</summary>
    public static int Height { get; private set; } = 1;

    /// <summary>
    /// Sets the current render surface size. Called by the host when the window or canvas is created and
    /// whenever it is resized. Values are clamped to at least 1 so a minimized (0×0) surface never yields a
    /// degenerate viewport or aspect ratio.
    /// </summary>
    /// <param name="width">The surface width in pixels.</param>
    /// <param name="height">The surface height in pixels.</param>
    public static void SetSize(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
    }
}
