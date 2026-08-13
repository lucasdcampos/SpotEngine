using System.Numerics;
using Spot.Rendering;

namespace Spot.UI;

/// <summary>
/// A rectangular container: a colored (or sprite-backed) background other widgets are placed on. With a
/// <see cref="Sprite"/> and a non-zero <see cref="Border"/> it draws as a nine-slice so panels resize without
/// distorting their edges.
/// </summary>
public class Panel : Widget
{
    /// <summary>The fill color when there is no sprite, or the tint multiplied with <see cref="Sprite"/>.</summary>
    public Vector4 Color = new(0.12f, 0.12f, 0.15f, 0.9f);

    /// <summary>An optional background sprite. When null the panel is a solid <see cref="Color"/> rectangle.</summary>
    public Texture2D? Sprite;

    /// <summary>The nine-slice border as <c>(left, top, right, bottom)</c> in texels; zero stretches the whole sprite.</summary>
    public Vector4 Border;

    /// <inheritdoc />
    protected override void OnDraw()
    {
        var pos = new Vector2(ScreenRect.X, ScreenRect.Y);
        var size = new Vector2(ScreenRect.Z, ScreenRect.W);

        if (Sprite is not null)
        {
            if (Border != Vector4.Zero) UIRenderer.DrawNineSlice(pos, size, Color, Sprite, Border);
            else UIRenderer.DrawQuad(pos, size, Color, Sprite, new Vector4(0f, 0f, 1f, 1f));
        }
        else if (Color.W > 0f)
        {
            UIRenderer.DrawQuad(pos, size, Color);
        }
    }
}
