using System.Collections.Generic;
using static Spot.Editor.UI.EditorPalette;

namespace Spot.Editor.UI;

/// <summary>
/// Built-in editor themes. Add new entries here (or build them at runtime) and hand them to
/// <see cref="EditorThemeManager.SetTheme"/> to switch the editor's look.
/// </summary>
public static class EditorThemes
{
    /// <summary>All built-in themes, used to populate the editor's theme menu.</summary>
    public static IReadOnlyList<EditorTheme> All => new[] { SpotDark };

    /// <summary>
    /// The default theme: a refined, low-contrast dark surface built as a gentle brightness ramp
    /// (window &lt; dock &lt; panel &lt; header &lt; control &lt; hover) with a single blue accent, soft
    /// borders and muted axis colors — tuned to feel like a commercial engine rather than a raw
    /// Dear ImGui app.
    /// </summary>
    public static EditorTheme SpotDark { get; } = new()
    {
        Name = "Spot Dark",
        Palette = new EditorPalette
        {
            // Surfaces climb in brightness so depth reads from tone, not heavy borders.
            WindowBg = Rgb(32, 33, 36),     // docked panels
            ChildBg = Rgb(29, 30, 33),      // inset regions (console output, asset grid)
            PopupBg = Rgb(37, 38, 42, 0.98f),
            HeaderBg = Rgb(43, 44, 49),     // menu bar, title bars, component header strips
            Border = Rgb(58, 58, 61, 0.55f),

            Text = Rgb(232, 232, 232),
            TextDisabled = Rgb(122, 122, 126),

            Accent = Rgb(77, 132, 255),
            AccentHovered = Rgb(107, 154, 255),
            AccentActive = Rgb(61, 112, 230),

            FrameBg = Rgb(46, 48, 53),
            FrameBgHovered = Rgb(60, 63, 70),
            FrameBgActive = Rgb(69, 72, 80),

            TitleBg = Rgb(26, 27, 29),
            TitleBgActive = Rgb(32, 33, 36),

            TabBg = Rgb(26, 27, 29),        // inactive tabs recede into the dock
            TabActive = Rgb(43, 44, 49),    // the selected tab lifts to the header tone
            TabHovered = Rgb(56, 58, 64),

            ScrollbarBg = Rgb(0, 0, 0, 0.0f),
            ScrollbarGrab = Rgb(62, 64, 70),

            Button = Rgb(52, 54, 60),
            ButtonHovered = Rgb(66, 69, 77),
            ButtonActive = Rgb(61, 112, 230),

            CheckMark = Rgb(77, 132, 255),
            SliderGrab = Rgb(77, 132, 255),
            Separator = Rgb(52, 53, 57),

            // Softer, desaturated axis colors so property fields read calm, not neon.
            AxisX = Rgb(196, 91, 94),
            AxisY = Rgb(122, 184, 110),
            AxisZ = Rgb(93, 137, 220),
            GizmoHover = Rgb(255, 214, 51),
            LogText = Rgb(200, 202, 208),
            LogCommand = Rgb(214, 190, 120),
            LogError = Rgb(240, 110, 110),
        },
        Metrics = new EditorStyleMetrics(),
    };
}
