using System;
using ImGuiNET;
using Spot.Engine;

namespace Spot.DebugUI.UI;

/// <summary>
/// Named access to the fonts the engine baked into the ImGui atlas. The editor registers them, in a
/// fixed order, through <see cref="ApplicationSpec.AdditionalFonts"/> (see <c>Program.cs</c>): index 0
/// is the body font, then a heavier <see cref="Title"/> face, a monospaced <see cref="Mono"/> face, a
/// large standalone icon face (<see cref="Icons"/>) for oversized glyphs, a large <see cref="IconText"/> face
/// for short words drawn as icons, and a <see cref="Small"/> face for captions.
/// Lookups fall back to the current font if an extra failed to load, so callers never get a null handle.
/// </summary>
public static class EditorFonts
{
    // Must match the registration order in Program.cs (body is the primary font at index 0).
    private const int TitleIndex = 1;
    private const int MonoIndex = 2;
    private const int IconsIndex = 3;
    private const int IconTextIndex = 4;
    private const int SmallIndex = 5;

    /// <summary>The primary UI font used for body text.</summary>
    public static ImFontPtr Body => Font(0);

    /// <summary>A slightly larger, heavier face for panel and component titles.</summary>
    public static ImFontPtr Title => Font(TitleIndex);

    /// <summary>A monospaced face for the console and other code/log text.</summary>
    public static ImFontPtr Mono => Font(MonoIndex);

    /// <summary>
    /// A large Font Awesome atlas (baked at 72px) for drawing <see cref="EditorIcons"/> glyphs at big
    /// sizes crisply — e.g. asset-browser tiles — via <c>drawList.AddText(EditorFonts.Icons, px, …)</c>.
    /// For inline, text-sized icons use the glyphs merged into <see cref="Body"/> instead.
    /// </summary>
    public static ImFontPtr Icons => Font(IconsIndex);

    /// <summary>
    /// A large text face (Inter Medium baked at 72px, only <see cref="IconTextGlyphRanges"/>) for short words
    /// drawn as icons at tile sizes, such as the "C#" on script tiles and "UI" on UI documents.
    /// </summary>
    public static ImFontPtr IconText => Font(IconTextIndex);

    /// <summary>The characters <see cref="IconText"/> bakes: <c>#</c>, the digits and the uppercase Latin letters.</summary>
    public static ushort[] IconTextGlyphRanges { get; } = { 0x0023, 0x0023, 0x0030, 0x0039, 0x0041, 0x005A, 0 };

    /// <summary>A smaller body face for secondary captions, e.g. the asset type under an asset-browser tile.</summary>
    public static ImFontPtr Small => Font(SmallIndex);

    private static ImFontPtr Font(int index)
    {
        var fonts = Application.Instance.Fonts;
        return index >= 0 && index < fonts.Count ? fonts[index] : ImGui.GetFont();
    }

    /// <summary>Pushes the <see cref="Title"/> font; pair with <see cref="Pop"/>.</summary>
    public static void PushTitle() => ImGui.PushFont(Title);

    /// <summary>Pushes the <see cref="Mono"/> font; pair with <see cref="Pop"/>.</summary>
    public static void PushMono() => ImGui.PushFont(Mono);

    /// <summary>Pops a font pushed by <see cref="PushTitle"/>/<see cref="PushMono"/>.</summary>
    public static void Pop() => ImGui.PopFont();
}
