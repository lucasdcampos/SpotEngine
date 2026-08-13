using System.Numerics;
using Spot.Rendering;

namespace Spot.UI;

/// <summary>
/// A clickable button: a background that tints on hover and press, with a centered label. Subscribe to
/// <see cref="OnClick"/>, which fires when the pointer is pressed and released over the button. With a
/// <see cref="Sprite"/> and non-zero <see cref="Border"/> the background draws as a nine-slice.
/// </summary>
public class Button : Widget
{
    /// <summary>The button label.</summary>
    public string Label = "";

    /// <summary>The label font, or null to use <see cref="UIRoot.DefaultFont"/>.</summary>
    public Font? Font;

    /// <summary>The label size in UI units.</summary>
    public float FontSize = 24f;

    /// <summary>The label color.</summary>
    public Vector4 TextColor = Vector4.One;

    /// <summary>The background color at rest.</summary>
    public Vector4 NormalColor = new(0.20f, 0.22f, 0.28f, 1f);

    /// <summary>The background color while hovered.</summary>
    public Vector4 HoverColor = new(0.28f, 0.31f, 0.38f, 1f);

    /// <summary>The background color while held down.</summary>
    public Vector4 PressedColor = new(0.14f, 0.16f, 0.20f, 1f);

    /// <summary>An optional background sprite tinted by the state colors.</summary>
    public Texture2D? Sprite;

    /// <summary>The nine-slice border as <c>(left, top, right, bottom)</c> in texels; zero stretches the whole sprite.</summary>
    public Vector4 Border;

    /// <summary>Raised when the button is clicked (pressed and released over it).</summary>
    public event Action? OnClick;

    /// <inheritdoc />
    protected override bool IsInteractive => true;

    /// <inheritdoc />
    protected override void OnDraw()
    {
        var pos = new Vector2(ScreenRect.X, ScreenRect.Y);
        var size = new Vector2(ScreenRect.Z, ScreenRect.W);
        Vector4 background = Held ? PressedColor : Hovered ? HoverColor : NormalColor;

        if (Sprite is not null)
        {
            if (Border != Vector4.Zero) UIRenderer.DrawNineSlice(pos, size, background, Sprite, Border);
            else UIRenderer.DrawQuad(pos, size, background, Sprite, new Vector4(0f, 0f, 1f, 1f));
        }
        else
        {
            UIRenderer.DrawQuad(pos, size, background);
        }

        Font? font = Font ?? UIRoot.DefaultFont;
        if (font is not null && !string.IsNullOrEmpty(Label))
        {
            var options = new TextLayoutOptions { Align = TextAlign.Center, LineSpacing = 1f, MaxWidth = size.X };
            Vector2 textSize = UIRenderer.MeasureText(font, Label, FontSize, options);
            var textPos = new Vector2(pos.X, pos.Y + (size.Y - textSize.Y) * 0.5f);
            UIRenderer.DrawText(font, Label, textPos, FontSize, TextColor, options);
        }
    }

    /// <inheritdoc />
    protected override void OnInteract(in UIInput input, bool over)
    {
        if (input.Released && over) OnClick?.Invoke();
    }
}
