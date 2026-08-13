using System.Numerics;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// A component that draws a string of text in the world, at the entity's transform. It is the world-space
/// counterpart to the screen-space UI text (<c>Spot.UI</c>): use it for floating labels, damage numbers,
/// signposts and nameplates. A render system reads it together with the entity's <see cref="TransformComponent"/>
/// and draws the text as camera-facing (or transform-oriented) quads through the shared font atlas.
/// </summary>
/// <remarks>
/// Like <see cref="Sprite2DComponent"/> this is data only — the engine's renderer does the drawing. When no
/// <see cref="Font"/> is set it falls back to the engine's built-in default font, so text renders with zero
/// setup.
/// </remarks>
[ComponentMenu("Text", Order = 15)]
[SceneComponent("Text")]
public sealed class TextComponent : Component
{
    /// <summary>
    /// Gets or sets the font to draw with. When <see langword="null"/> the engine's built-in default font is
    /// used, so text still renders without assigning one.
    /// </summary>
    [AssetReference(nameof(FontPath))]
    public Font? Font { get; set; }

    /// <summary>Gets or sets the path/reference to the font asset, used for serialization.</summary>
    [HideInInspector]
    public string? FontPath { get; set; }

    /// <summary>Gets or sets the text to draw. Supports explicit line breaks (<c>\n</c>).</summary>
    public string Text { get; set; } = "Text";

    /// <summary>Gets or sets the color (RGBA) the text is tinted with. Defaults to opaque white.</summary>
    [InspectorColor]
    public Vector4 Color { get; set; } = Vector4.One;

    /// <summary>
    /// Gets or sets the font size, in the same base pixels the glyphs were rasterized at. Combined with
    /// <see cref="WorldScale"/> it controls how large the text appears in the world.
    /// </summary>
    public float FontSize { get; set; } = 48f;

    /// <summary>Gets or sets the horizontal alignment of each line about the entity's position.</summary>
    public TextAlign Alignment { get; set; } = TextAlign.Center;

    /// <summary>
    /// Gets or sets whether the text always faces the camera. When <see langword="false"/> the text lies on
    /// the entity's local XY plane and is oriented by its transform (useful for text painted onto surfaces).
    /// </summary>
    public bool Billboard { get; set; } = true;

    /// <summary>
    /// Gets or sets the world units per font pixel. The default (0.01) makes a 48px string about 0.48 world
    /// units tall — legible without dwarfing typical scenes.
    /// </summary>
    public float WorldScale { get; set; } = 0.01f;
}
