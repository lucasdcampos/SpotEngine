using System.Numerics;
using Spot.Rendering;

namespace Spot.UI;

/// <summary>
/// Draws a string inside its rectangle. Uses <see cref="Font"/>, falling back to <see cref="UIRoot.DefaultFont"/>,
/// so most text needs only a string and a size. Alignment is applied within the widget's width; set
/// <see cref="Wrap"/> to break long lines at that width.
/// </summary>
public class Text : Widget
{
    /// <summary>The text to draw.</summary>
    public string Content = "";

    /// <summary>The font to draw with, or null to use <see cref="UIRoot.DefaultFont"/>.</summary>
    public Font? Font;

    /// <summary>The text size in UI units.</summary>
    public float FontSize = 24f;

    /// <summary>The text color.</summary>
    public Vector4 Color = Vector4.One;

    /// <summary>Horizontal alignment within the widget's width.</summary>
    public TextAlign Align = TextAlign.Left;

    /// <summary>Whether to wrap long lines at the widget's width.</summary>
    public bool Wrap;

    /// <inheritdoc />
    protected override void OnDraw()
    {
        Font? font = Font ?? UIRoot.DefaultFont;
        if (font is null || string.IsNullOrEmpty(Content)) return;

        var options = new TextLayoutOptions
        {
            Align = Align,
            LineSpacing = 1f,
            // Alignment other than left needs a box to align within; wrapping needs one to break at.
            MaxWidth = Wrap || Align != TextAlign.Left ? ScreenRect.Z : 0f,
        };

        UIRenderer.DrawText(font, Content, new Vector2(ScreenRect.X, ScreenRect.Y), FontSize, Color, options);
    }
}
