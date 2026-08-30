using System.Numerics;
using Spot.Rendering;

namespace Spot.UI;

/// <summary>
/// Displays a texture (or a solid color when it has none). With a non-zero <see cref="Border"/> it draws as a
/// nine-slice, keeping its corners crisp while the edges and center stretch.
/// </summary>
public class Image : Widget
{
    /// <summary>The texture to display. When null the widget is a solid <see cref="Color"/> rectangle.</summary>
    public Texture2D? Texture;

    /// <summary>
    /// The stored reference (source path or <c>guid:</c>) for <see cref="Texture"/>, used to serialize the
    /// image in a <c>.sptui</c> document and re-load it. Set by the editor and the loader; code that assigns
    /// <see cref="Texture"/> directly can leave it empty.
    /// </summary>
    public string TextureRef = "";

    /// <summary>The tint multiplied with the texture, or the fill color when there is no texture.</summary>
    public Vector4 Color = Vector4.One;

    /// <summary>The nine-slice border as <c>(left, top, right, bottom)</c> in texels; zero stretches the whole texture.</summary>
    public Vector4 Border;

    /// <inheritdoc />
    protected override void OnDraw()
    {
        var pos = new Vector2(ScreenRect.X, ScreenRect.Y);
        var size = new Vector2(ScreenRect.Z, ScreenRect.W);

        if (Texture is null)
        {
            if (Color.W > 0f) UIRenderer.DrawQuad(pos, size, Color);
            return;
        }

        if (Border != Vector4.Zero) UIRenderer.DrawNineSlice(pos, size, Color, Texture, Border);
        else UIRenderer.DrawQuad(pos, size, Color, Texture, new Vector4(0f, 0f, 1f, 1f));
    }
}
