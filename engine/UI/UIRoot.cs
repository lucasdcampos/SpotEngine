using System.Numerics;
using Spot.Rendering;

namespace Spot.UI;

/// <summary>How the UI surface maps its coordinates onto the actual screen.</summary>
public enum UIScaleMode
{
    /// <summary>One UI unit is one screen pixel. Widgets keep their pixel size regardless of resolution.</summary>
    ConstantPixel,

    /// <summary>The UI is scaled so <see cref="UIRoot.ReferenceHeight"/> always fills the screen height, keeping
    /// layouts proportional across resolutions (width in UI units varies with the aspect ratio).</summary>
    ScaleWithHeight,
}

/// <summary>
/// The root of a scene's UI tree and its input dispatcher. Each scene owns one (see <c>Scene.UI</c>): scripts
/// add widgets to it, the scene ticks <see cref="Update"/> every play-mode frame to route pointer input, and
/// the render system calls <see cref="Render"/> as the final screen-space pass. The root also owns the scaling
/// mode that converts between screen pixels and the UI coordinate space widgets lay out in.
/// </summary>
public sealed class UIRoot : Widget
{
    private static Font? s_defaultFont;

    /// <summary>
    /// The font used by text widgets that do not set one of their own. Defaults to the engine's built-in
    /// <see cref="Font.Default"/> (created on first use), so text renders without any setup; assign a game
    /// font here at startup to override it everywhere.
    /// </summary>
    public static Font DefaultFont
    {
        get => s_defaultFont ??= Font.Default;
        set => s_defaultFont = value;
    }

    /// <summary>How UI coordinates map to the screen. Defaults to <see cref="UIScaleMode.ScaleWithHeight"/>.</summary>
    public UIScaleMode ScaleMode = UIScaleMode.ScaleWithHeight;

    /// <summary>The design height, in UI units, kept filling the screen under <see cref="UIScaleMode.ScaleWithHeight"/>.</summary>
    public float ReferenceHeight = 1080f;

    private Widget? _captured;

    /// <summary>The scale factor from UI units to screen pixels applied by the last update or render.</summary>
    public float Scale { get; private set; } = 1f;

    /// <summary>The UI surface width in UI units (varies with aspect ratio under scale-with-height).</summary>
    public float Width { get; private set; }

    /// <summary>The UI surface height in UI units.</summary>
    public float Height { get; private set; }

    /// <summary>Whether the pointer is currently over an interactive widget, so the game can ignore world clicks.</summary>
    public bool WantsPointer { get; private set; }

    /// <summary>
    /// Routes one frame of pointer input through the tree: lays it out, finds the widget under the pointer,
    /// captures it on press so drags keep tracking, and fires widget callbacks (clicks, value changes).
    /// </summary>
    /// <param name="screenWidth">The surface width in screen pixels.</param>
    /// <param name="screenHeight">The surface height in screen pixels.</param>
    /// <param name="mouseScreen">The pointer position in screen pixels (top-left origin).</param>
    /// <param name="down">Whether the primary button is held.</param>
    /// <param name="pressed">Whether the primary button went down this frame.</param>
    /// <param name="released">Whether the primary button came up this frame.</param>
    public void Update(float screenWidth, float screenHeight, Vector2 mouseScreen, bool down, bool pressed, bool released)
    {
        UpdateScale(screenWidth, screenHeight);
        Layout(new Vector4(0f, 0f, Width, Height));

        Vector2 pointer = Scale > 0f ? mouseScreen / Scale : mouseScreen;

        ResetInteractionState();
        Widget? hovered = HitTest(pointer);

        if (pressed && hovered is not null)
        {
            _captured = hovered;
        }

        Widget? target = _captured ?? hovered;
        if (target is not null)
        {
            bool over = Contains(target.ScreenRect, pointer);
            target.Dispatch(new UIInput(pointer, down, pressed, released), over);
        }

        if (released)
        {
            _captured = null;
        }

        WantsPointer = hovered is not null || _captured is not null;
    }

    /// <summary>
    /// Draws the whole tree as a screen-space pass. Does nothing when the tree is empty, so scenes without UI
    /// pay nothing.
    /// </summary>
    /// <param name="screenWidth">The surface width in screen pixels.</param>
    /// <param name="screenHeight">The surface height in screen pixels.</param>
    public void Render(float screenWidth, float screenHeight)
    {
        if (Children.Count == 0) return;

        UpdateScale(screenWidth, screenHeight);
        Layout(new Vector4(0f, 0f, Width, Height));

        UIRenderer.Begin(Width, Height);
        RenderTree();
        UIRenderer.End();
    }

    private void UpdateScale(float screenWidth, float screenHeight)
    {
        if (ScaleMode == UIScaleMode.ScaleWithHeight && ReferenceHeight > 0f && screenHeight > 0f)
        {
            Scale = screenHeight / ReferenceHeight;
            if (Scale <= 0f) Scale = 1f;
        }
        else
        {
            Scale = 1f;
        }

        Width = Scale > 0f ? screenWidth / Scale : screenWidth;
        Height = Scale > 0f ? screenHeight / Scale : screenHeight;

        Rect.Anchor = Vector2.Zero;
        Rect.Pivot = Vector2.Zero;
        Rect.Position = Vector2.Zero;
        Rect.Size = new Vector2(Width, Height);
    }
}
