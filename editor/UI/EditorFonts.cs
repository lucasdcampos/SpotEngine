using System;
using ImGuiNET;
using Spot.Core;

namespace Spot.Editor.UI;

/// <summary>
/// Named access to the fonts the engine baked into the ImGui atlas. The editor registers them, in a
/// fixed order, through <see cref="ApplicationSpec.AdditionalFonts"/> (see <c>Program.cs</c>): index 0
/// is the body font, then a heavier <see cref="Title"/> face and a monospaced <see cref="Mono"/> face.
/// Lookups fall back to the current font if an extra failed to load, so callers never get a null handle.
/// </summary>
public static class EditorFonts
{
    // Must match the registration order in Program.cs (body is the primary font at index 0).
    private const int TitleIndex = 1;
    private const int MonoIndex = 2;

    /// <summary>The primary UI font used for body text.</summary>
    public static ImFontPtr Body => Font(0);

    /// <summary>A slightly larger, heavier face for panel and component titles.</summary>
    public static ImFontPtr Title => Font(TitleIndex);

    /// <summary>A monospaced face for the console and other code/log text.</summary>
    public static ImFontPtr Mono => Font(MonoIndex);

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
