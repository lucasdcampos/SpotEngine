using System.Collections.Generic;
using static Spot.Engine.Debug.UI.EditorPalette;

namespace Spot.Engine.Debug.UI;

/// <summary>
/// Built-in editor themes. Add new entries here (or build them at runtime) and hand them to
/// <see cref="EditorThemeManager.SetTheme"/> to switch the editor's look.
/// </summary>
public static class EditorThemes
{
    /// <summary>All built-in themes, used to populate the editor's theme menu.</summary>
    public static IReadOnlyList<EditorTheme> All => new[] { SpotDark, SpotLight, Nord, Cherry, AllBlack };

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
            WindowBg = Rgb(21, 21, 21),     // docked panels
            ChildBg = Rgb(15, 15, 15),      // inset regions (console output, asset grid)
            PopupBg = Rgb(30, 30, 30, 0.98f),
            HeaderBg = Rgb(26, 26, 26),     // menu bar, title bars, component header strips
            Border = Rgb(40, 40, 40, 0.8f),

            Text = Rgb(200, 200, 200),
            TextDisabled = Rgb(100, 100, 100),

            Accent = Rgb(224, 112, 0),
            AccentHovered = Rgb(249, 137, 25),
            AccentActive = Rgb(170, 85, 0),

            FrameBg = Rgb(10, 10, 10),
            FrameBgHovered = Rgb(35, 35, 35),
            FrameBgActive = Rgb(45, 45, 45),

            TitleBg = Rgb(15, 15, 15),
            TitleBgActive = Rgb(21, 21, 21),

            TabBg = Rgb(15, 15, 15),        // inactive tabs recede into the dock
            TabActive = Rgb(35, 35, 35),    // the selected tab lifts to the header tone
            TabHovered = Rgb(45, 45, 45),

            ScrollbarBg = Rgb(0, 0, 0, 0.0f),
            ScrollbarGrab = Rgb(45, 45, 45),

            Button = Rgb(35, 35, 35),
            ButtonHovered = Rgb(50, 50, 50),
            ButtonActive = Rgb(224, 112, 0),

            CheckMark = Rgb(224, 112, 0),
            SliderGrab = Rgb(224, 112, 0),
            Separator = Rgb(35, 35, 35),

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

    public static EditorTheme SpotLight { get; } = new()
    {
        Name = "Spot Light",
        Palette = new EditorPalette
        {
            WindowBg = Rgb(240, 240, 240),
            ChildBg = Rgb(220, 220, 220),
            PopupBg = Rgb(245, 245, 245, 0.98f),
            HeaderBg = Rgb(230, 230, 230),
            Border = Rgb(200, 200, 200, 0.55f),

            Text = Rgb(20, 20, 20),
            TextDisabled = Rgb(120, 120, 120),

            Accent = Rgb(200, 100, 30),
            AccentHovered = Rgb(220, 120, 50),
            AccentActive = Rgb(180, 80, 20),

            FrameBg = Rgb(250, 250, 250),
            FrameBgHovered = Rgb(220, 220, 230),
            FrameBgActive = Rgb(200, 200, 210),

            TitleBg = Rgb(210, 210, 210),
            TitleBgActive = Rgb(230, 230, 230),

            TabBg = Rgb(210, 210, 210),
            TabActive = Rgb(240, 240, 240),
            TabHovered = Rgb(230, 230, 230),

            ScrollbarBg = Rgb(200, 200, 200, 0.1f),
            ScrollbarGrab = Rgb(180, 180, 180),

            Button = Rgb(220, 220, 220),
            ButtonHovered = Rgb(200, 200, 200),
            ButtonActive = Rgb(180, 180, 180),

            CheckMark = Rgb(200, 100, 30),
            SliderGrab = Rgb(200, 100, 30),
            Separator = Rgb(200, 200, 200),

            AxisX = Rgb(196, 91, 94),
            AxisY = Rgb(122, 184, 110),
            AxisZ = Rgb(93, 137, 220),
            GizmoHover = Rgb(255, 214, 51),
            LogText = Rgb(20, 20, 20),
            LogCommand = Rgb(100, 100, 150),
            LogError = Rgb(200, 50, 50),
        },
        Metrics = new EditorStyleMetrics(),
    };

    public static EditorTheme Nord { get; } = new()
    {
        Name = "Nord",
        Palette = new EditorPalette
        {
            WindowBg = Rgb(46, 52, 64),
            ChildBg = Rgb(59, 66, 82),
            PopupBg = Rgb(67, 76, 94, 0.98f),
            HeaderBg = Rgb(76, 86, 106),
            Border = Rgb(59, 66, 82),

            Text = Rgb(216, 222, 233),
            TextDisabled = Rgb(143, 188, 187),

            Accent = Rgb(136, 192, 208),
            AccentHovered = Rgb(129, 161, 193),
            AccentActive = Rgb(94, 129, 172),

            FrameBg = Rgb(59, 66, 82),
            FrameBgHovered = Rgb(67, 76, 94),
            FrameBgActive = Rgb(76, 86, 106),

            TitleBg = Rgb(46, 52, 64),
            TitleBgActive = Rgb(59, 66, 82),

            TabBg = Rgb(46, 52, 64),
            TabActive = Rgb(67, 76, 94),
            TabHovered = Rgb(76, 86, 106),

            ScrollbarBg = Rgb(46, 52, 64, 0.0f),
            ScrollbarGrab = Rgb(76, 86, 106),

            Button = Rgb(67, 76, 94),
            ButtonHovered = Rgb(76, 86, 106),
            ButtonActive = Rgb(94, 129, 172),

            CheckMark = Rgb(136, 192, 208),
            SliderGrab = Rgb(136, 192, 208),
            Separator = Rgb(67, 76, 94),

            AxisX = Rgb(191, 97, 106),
            AxisY = Rgb(163, 190, 140),
            AxisZ = Rgb(136, 192, 208),
            GizmoHover = Rgb(235, 203, 139),
            LogText = Rgb(236, 239, 244),
            LogCommand = Rgb(180, 142, 173),
            LogError = Rgb(191, 97, 106),
        },
        Metrics = new EditorStyleMetrics(),
    };

    public static EditorTheme Cherry { get; } = new()
    {
        Name = "Cherry",
        Palette = new EditorPalette
        {
            WindowBg = Rgb(38, 28, 30),
            ChildBg = Rgb(46, 32, 35),
            PopupBg = Rgb(54, 38, 42, 0.98f),
            HeaderBg = Rgb(61, 41, 46),
            Border = Rgb(74, 46, 52),

            Text = Rgb(238, 224, 226),
            TextDisabled = Rgb(153, 135, 138),

            Accent = Rgb(219, 88, 115),
            AccentHovered = Rgb(232, 105, 132),
            AccentActive = Rgb(194, 66, 92),

            FrameBg = Rgb(54, 38, 42),
            FrameBgHovered = Rgb(69, 48, 53),
            FrameBgActive = Rgb(82, 58, 64),

            TitleBg = Rgb(33, 24, 26),
            TitleBgActive = Rgb(43, 31, 33),

            TabBg = Rgb(33, 24, 26),
            TabActive = Rgb(61, 41, 46),
            TabHovered = Rgb(74, 46, 52),

            ScrollbarBg = Rgb(0, 0, 0, 0.0f),
            ScrollbarGrab = Rgb(82, 58, 64),

            Button = Rgb(69, 48, 53),
            ButtonHovered = Rgb(82, 58, 64),
            ButtonActive = Rgb(194, 66, 92),

            CheckMark = Rgb(219, 88, 115),
            SliderGrab = Rgb(219, 88, 115),
            Separator = Rgb(69, 48, 53),

            AxisX = Rgb(219, 88, 115),
            AxisY = Rgb(138, 189, 130),
            AxisZ = Rgb(102, 153, 204),
            GizmoHover = Rgb(235, 203, 139),
            LogText = Rgb(238, 224, 226),
            LogCommand = Rgb(204, 153, 179),
            LogError = Rgb(219, 88, 115),
        },
        Metrics = new EditorStyleMetrics(),
    };

    public static EditorTheme AllBlack { get; } = new()
    {
        Name = "All Black",
        Palette = new EditorPalette
        {
            WindowBg = Rgb(0, 0, 0),
            ChildBg = Rgb(0, 0, 0),
            PopupBg = Rgb(5, 5, 5, 0.98f),
            HeaderBg = Rgb(10, 10, 10),
            Border = Rgb(40, 40, 40, 0.55f),

            Text = Rgb(240, 240, 240),
            TextDisabled = Rgb(100, 100, 100),

            Accent = Rgb(255, 255, 255),
            AccentHovered = Rgb(200, 200, 200),
            AccentActive = Rgb(150, 150, 150),

            FrameBg = Rgb(15, 15, 15),
            FrameBgHovered = Rgb(30, 30, 30),
            FrameBgActive = Rgb(45, 45, 45),

            TitleBg = Rgb(0, 0, 0),
            TitleBgActive = Rgb(10, 10, 10),

            TabBg = Rgb(0, 0, 0),
            TabActive = Rgb(15, 15, 15),
            TabHovered = Rgb(25, 25, 25),

            ScrollbarBg = Rgb(0, 0, 0, 0.0f),
            ScrollbarGrab = Rgb(40, 40, 40),

            Button = Rgb(15, 15, 15),
            ButtonHovered = Rgb(30, 30, 30),
            ButtonActive = Rgb(45, 45, 45),

            CheckMark = Rgb(255, 255, 255),
            SliderGrab = Rgb(255, 255, 255),
            Separator = Rgb(30, 30, 30),

            AxisX = Rgb(255, 50, 50),
            AxisY = Rgb(50, 255, 50),
            AxisZ = Rgb(50, 100, 255),
            GizmoHover = Rgb(255, 255, 0),
            LogText = Rgb(240, 240, 240),
            LogCommand = Rgb(150, 150, 150),
            LogError = Rgb(255, 50, 50),
        },
        Metrics = new EditorStyleMetrics(),
    };
}
