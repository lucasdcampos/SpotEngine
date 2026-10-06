using System.Numerics;

namespace Spot.Engine;

/// <summary>
/// The size of the app's view in pixels — the desktop window's client area, the browser canvas, or the part of a
/// host's window the app is shown in. It is a backend-neutral seam so engine code (camera viewport sizing, UI
/// layout and hit-testing) can read the drawable dimensions without depending on the desktop <see cref="Window"/>.
/// The active host keeps it current: the desktop window updates it on creation and resize; the browser host updates
/// it from the canvas.
/// </summary>
/// <remarks>
/// A host that presents the app in a sub-rectangle of its own window — the editor playing a game inside a scene
/// viewport — sets that rectangle with <see cref="SetView"/>. <see cref="Width"/> and <see cref="Height"/> then
/// report the view, and <see cref="Input.MousePosition"/> is measured from its top-left corner, so the app's UI and
/// picking work exactly as they do full-window.
/// </remarks>
public static class Display
{
    private static int s_surfaceWidth = 1;
    private static int s_surfaceHeight = 1;
    private static bool s_hasView;
    private static Vector2 s_viewOrigin;
    private static int s_viewWidth = 1;
    private static int s_viewHeight = 1;

    /// <summary>Gets the width of the app's view in pixels (never less than 1).</summary>
    public static int Width => s_hasView ? s_viewWidth : s_surfaceWidth;

    /// <summary>Gets the height of the app's view in pixels (never less than 1).</summary>
    public static int Height => s_hasView ? s_viewHeight : s_surfaceHeight;

    /// <summary>
    /// Gets the top-left corner of the app's view within the window, in pixels: zero unless a host set a view with
    /// <see cref="SetView"/>.
    /// </summary>
    public static Vector2 ViewOrigin => s_hasView ? s_viewOrigin : Vector2.Zero;

    /// <summary>Gets whether a host is presenting the app in a sub-rectangle of its window (see <see cref="SetView"/>).</summary>
    public static bool HasView => s_hasView;

    /// <summary>
    /// Sets the current render surface size. Called by the host when the window or canvas is created and
    /// whenever it is resized. Values are clamped to at least 1 so a minimized (0×0) surface never yields a
    /// degenerate viewport or aspect ratio. A view set with <see cref="SetView"/> keeps precedence.
    /// </summary>
    /// <param name="width">The surface width in pixels.</param>
    /// <param name="height">The surface height in pixels.</param>
    public static void SetSize(int width, int height)
    {
        s_surfaceWidth = Math.Max(1, width);
        s_surfaceHeight = Math.Max(1, height);
    }

    /// <summary>
    /// Presents the app in a rectangle of the window rather than the whole of it: <see cref="Width"/> and
    /// <see cref="Height"/> report the rectangle and <see cref="Input.MousePosition"/> is measured from its corner.
    /// For hosts such as an editor that draw the app inside one of their panels; undo it with
    /// <see cref="ClearView"/>.
    /// </summary>
    /// <param name="x">The left edge, in window pixels.</param>
    /// <param name="y">The top edge, in window pixels.</param>
    /// <param name="width">The width in pixels (at least 1).</param>
    /// <param name="height">The height in pixels (at least 1).</param>
    public static void SetView(float x, float y, float width, float height)
    {
        s_hasView = true;
        s_viewOrigin = new Vector2(x, y);
        s_viewWidth = Math.Max(1, (int)MathF.Round(width));
        s_viewHeight = Math.Max(1, (int)MathF.Round(height));
    }

    /// <summary>Returns to presenting the app in the whole window (undoes <see cref="SetView"/>).</summary>
    public static void ClearView() => s_hasView = false;
}
