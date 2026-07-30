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
    /// The default theme: a neutral dark grey surface with a blue accent, gentle rounding and
    /// subtle borders.
    /// </summary>
    public static EditorTheme SpotDark { get; } = new()
    {
        Name = "Spot Dark",
        Palette = new EditorPalette
        {
            WindowBg = Rgb(30, 30, 30),
            ChildBg = Rgb(37, 37, 38),
            PopupBg = Rgb(27, 27, 28, 0.98f),
            HeaderBg = Rgb(45, 45, 48),
            Border = Rgb(58, 58, 61, 0.5f),

            Text = Rgb(228, 228, 228),
            TextDisabled = Rgb(128, 128, 128),

            Accent = Rgb(59, 130, 246),
            AccentHovered = Rgb(90, 153, 255),
            AccentActive = Rgb(46, 107, 217),

            FrameBg = Rgb(42, 42, 44),
            FrameBgHovered = Rgb(56, 56, 60),
            FrameBgActive = Rgb(64, 64, 70),

            TitleBg = Rgb(26, 26, 26),
            TitleBgActive = Rgb(37, 37, 38),

            TabBg = Rgb(37, 37, 38),
            TabActive = Rgb(51, 56, 68),
            TabHovered = Rgb(66, 92, 133),

            ScrollbarBg = Rgb(26, 26, 26, 0.6f),
            ScrollbarGrab = Rgb(77, 77, 82),

            Button = Rgb(51, 51, 54),
            ButtonHovered = Rgb(69, 69, 74),
            ButtonActive = Rgb(59, 130, 246),

            CheckMark = Rgb(59, 130, 246),
            SliderGrab = Rgb(59, 130, 246),
            Separator = Rgb(64, 64, 68),

            AxisX = Rgb(229, 57, 53),
            AxisY = Rgb(76, 217, 100),
            AxisZ = Rgb(66, 133, 244),
            GizmoHover = Rgb(255, 214, 51),
            LogText = Rgb(229, 229, 229),
            LogCommand = Rgb(217, 217, 51),
            LogError = Rgb(255, 89, 89),
        },
        Metrics = new EditorStyleMetrics(),
    };
}
