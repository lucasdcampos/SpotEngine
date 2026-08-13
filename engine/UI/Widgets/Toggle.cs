using System.Numerics;
using Spot.Rendering;

namespace Spot.UI;

/// <summary>
/// A checkbox with an optional label. Clicking flips <see cref="On"/> and raises <see cref="OnValueChanged"/>.
/// The box is drawn at the left, sized to the widget's height, with the label to its right.
/// </summary>
public class Toggle : Widget
{
    /// <summary>Whether the toggle is currently on.</summary>
    public bool On;

    /// <summary>The label drawn to the right of the box.</summary>
    public string Label = "";

    /// <summary>The label font, or null to use <see cref="UIRoot.DefaultFont"/>.</summary>
    public Font? Font;

    /// <summary>The label size in UI units.</summary>
    public float FontSize = 22f;

    /// <summary>The label color.</summary>
    public Vector4 TextColor = Vector4.One;

    /// <summary>The box (unchecked) color.</summary>
    public Vector4 BoxColor = new(0.12f, 0.13f, 0.16f, 1f);

    /// <summary>The box color while hovered.</summary>
    public Vector4 HoverColor = new(0.18f, 0.20f, 0.25f, 1f);

    /// <summary>The check-mark fill color when on.</summary>
    public Vector4 CheckColor = new(0.30f, 0.75f, 0.45f, 1f);

    /// <summary>Raised with the new state whenever it changes.</summary>
    public event Action<bool>? OnValueChanged;

    /// <inheritdoc />
    protected override bool IsInteractive => true;

    /// <inheritdoc />
    protected override void OnDraw()
    {
        var pos = new Vector2(ScreenRect.X, ScreenRect.Y);
        var size = new Vector2(ScreenRect.Z, ScreenRect.W);
        float box = MathF.Min(size.Y, size.X);

        UIRenderer.DrawQuad(pos, new Vector2(box, box), Hovered ? HoverColor : BoxColor);
        if (On)
        {
            float pad = box * 0.25f;
            UIRenderer.DrawQuad(new Vector2(pos.X + pad, pos.Y + pad), new Vector2(box - 2f * pad, box - 2f * pad), CheckColor);
        }

        Font? font = Font ?? UIRoot.DefaultFont;
        if (font is not null && !string.IsNullOrEmpty(Label))
        {
            var options = new TextLayoutOptions { Align = TextAlign.Left, LineSpacing = 1f, MaxWidth = 0f };
            var textPos = new Vector2(pos.X + box + 8f, pos.Y + (box - FontSize) * 0.5f);
            UIRenderer.DrawText(font, Label, textPos, FontSize, TextColor, options);
        }
    }

    /// <inheritdoc />
    protected override void OnInteract(in UIInput input, bool over)
    {
        if (input.Released && over)
        {
            On = !On;
            OnValueChanged?.Invoke(On);
        }
    }
}
