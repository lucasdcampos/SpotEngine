using System.Numerics;
using ImGuiNET;

namespace Spot.DebugUI.UI;

/// <summary>
/// A named set of colors used by the editor. Every field is a plain <see cref="Vector4"/> (RGBA,
/// components in the 0..1 range) so a theme can be created, edited at runtime, and serialized to
/// disk without any ImGui dependency. Semantic names are used instead of ImGui's raw color slots so
/// that future custom themes only need to fill in meaningful values.
/// </summary>
public sealed class EditorPalette
{
    // Surfaces.
    public Vector4 WindowBg;
    public Vector4 ChildBg;
    public Vector4 PopupBg;
    public Vector4 HeaderBg;      // Menu bar / title / tab strip background.
    public Vector4 Border;

    // Text.
    public Vector4 Text;
    public Vector4 TextDisabled;

    // Accent (used for selection, active tabs, checkmarks, sliders...).
    public Vector4 Accent;
    public Vector4 AccentHovered;
    public Vector4 AccentActive;

    // Input frames.
    public Vector4 FrameBg;
    public Vector4 FrameBgHovered;
    public Vector4 FrameBgActive;

    // Title bars.
    public Vector4 TitleBg;
    public Vector4 TitleBgActive;

    // Tabs.
    public Vector4 TabBg;
    public Vector4 TabActive;
    public Vector4 TabHovered;

    // Scrollbar.
    public Vector4 ScrollbarBg;
    public Vector4 ScrollbarGrab;

    // Buttons.
    public Vector4 Button;
    public Vector4 ButtonHovered;
    public Vector4 ButtonActive;

    // Misc widgets.
    public Vector4 CheckMark;
    public Vector4 SliderGrab;
    public Vector4 Separator;

    // Application-specific colors (not part of the raw ImGui style).
    public Vector4 AxisX;
    public Vector4 AxisY;
    public Vector4 AxisZ;
    public Vector4 GizmoHover;
    public Vector4 LogText;
    public Vector4 LogCommand;
    public Vector4 LogError;

    /// <summary>Builds a color from 0..255 channel values.</summary>
    public static Vector4 Rgb(int r, int g, int b, float a = 1.0f) =>
        new(r / 255.0f, g / 255.0f, b / 255.0f, a);
}

/// <summary>
/// Spacing, rounding and border metrics for a theme. Kept separate from the palette so both can be
/// tweaked and serialized independently.
/// </summary>
public sealed class EditorStyleMetrics
{
    // One small rounding scale for the whole editor: 2 for grabs, 3 for controls, 4 for surfaces
    // (windows, popups, cards, tabs). Scrollbars are pills.
    public float WindowRounding = 4.0f;
    public float ChildRounding = 4.0f;
    public float FrameRounding = 3.0f;
    public float PopupRounding = 4.0f;
    public float TabRounding = 4.0f;
    public float GrabRounding = 2.0f;
    public float ScrollbarRounding = 8.0f;

    public float WindowBorderSize = 1.0f;
    public float FrameBorderSize = 0.0f;
    public float PopupBorderSize = 1.0f;
    public float TabBorderSize = 0.0f;
    public float TabBarBorderSize = 1.0f;

    /// <summary>Width of the splitter between docked panels; drawn in the palette's separator tone.</summary>
    public float DockingSeparatorSize = 2.0f;

    public Vector2 WindowPadding = new(10.0f, 8.0f);
    public Vector2 FramePadding = new(8.0f, 4.0f);
    public Vector2 ItemSpacing = new(8.0f, 5.0f);
    public Vector2 ItemInnerSpacing = new(5.0f, 4.0f);
    public Vector2 CellPadding = new(6.0f, 3.0f);

    public float ScrollbarSize = 10.0f;
    public float GrabMinSize = 10.0f;
    public float IndentSpacing = 16.0f;
}

/// <summary>
/// A complete editor theme: a name, a color palette and style metrics. Call <see cref="Apply"/> to
/// push the theme onto the active ImGui context (and mirror the relevant colors onto engine-owned
/// UI such as the developer console).
/// </summary>
public sealed class EditorTheme
{
    public required string Name { get; init; }
    public required EditorPalette Palette { get; init; }
    public EditorStyleMetrics Metrics { get; init; } = new();

    /// <summary>
    /// Writes this theme's colors and metrics into the current ImGui style. Requires an active ImGui
    /// context, so call it after the ImGui controller has been created (for example from a scene's
    /// <c>OnEnter</c>).
    /// </summary>
    public void Apply()
    {
        var style = ImGui.GetStyle();
        var p = Palette;

        // Reset to ImGui's dark preset first so any slot we do not explicitly set has a sane value.
        ImGui.StyleColorsDark(style);

        void Set(ImGuiCol slot, Vector4 color) => style.Colors[(int)slot] = color;

        Set(ImGuiCol.Text, p.Text);
        Set(ImGuiCol.TextDisabled, p.TextDisabled);
        Set(ImGuiCol.TextSelectedBg, WithAlpha(p.Accent, 0.35f));

        Set(ImGuiCol.WindowBg, p.WindowBg);
        Set(ImGuiCol.ChildBg, p.ChildBg);
        Set(ImGuiCol.PopupBg, p.PopupBg);
        Set(ImGuiCol.Border, p.Border);
        Set(ImGuiCol.BorderShadow, new Vector4(0, 0, 0, 0));

        Set(ImGuiCol.FrameBg, p.FrameBg);
        Set(ImGuiCol.FrameBgHovered, p.FrameBgHovered);
        Set(ImGuiCol.FrameBgActive, p.FrameBgActive);

        Set(ImGuiCol.TitleBg, p.TitleBg);
        Set(ImGuiCol.TitleBgActive, p.TitleBgActive);
        Set(ImGuiCol.TitleBgCollapsed, p.TitleBg);
        Set(ImGuiCol.MenuBarBg, p.HeaderBg);

        Set(ImGuiCol.ScrollbarBg, p.ScrollbarBg);
        Set(ImGuiCol.ScrollbarGrab, p.ScrollbarGrab);
        Set(ImGuiCol.ScrollbarGrabHovered, Lighten(p.ScrollbarGrab, 0.08f));
        Set(ImGuiCol.ScrollbarGrabActive, Lighten(p.ScrollbarGrab, 0.16f));

        Set(ImGuiCol.CheckMark, p.CheckMark);
        Set(ImGuiCol.SliderGrab, p.SliderGrab);
        Set(ImGuiCol.SliderGrabActive, p.AccentHovered);

        Set(ImGuiCol.Button, p.Button);
        Set(ImGuiCol.ButtonHovered, p.ButtonHovered);
        Set(ImGuiCol.ButtonActive, p.ButtonActive);

        // Header slots drive selectables, tree nodes, menus and combos. The selected row is a muted
        // accent wash rather than a solid fill, and hover is a neutral lift, so a selection reads as
        // "this one" without shouting and hovering never looks like a second selection.
        Set(ImGuiCol.Header, WithAlpha(p.Accent, 0.40f));
        Set(ImGuiCol.HeaderHovered, WithAlpha(p.Text, 0.08f));
        Set(ImGuiCol.HeaderActive, WithAlpha(p.Accent, 0.55f));

        // The separator tone also paints the splitters between docked panels (DockingSeparatorSize wide),
        // which light up in the accent while hovered or dragged.
        Set(ImGuiCol.Separator, p.Separator);
        Set(ImGuiCol.SeparatorHovered, WithAlpha(p.Accent, 0.70f));
        Set(ImGuiCol.SeparatorActive, p.Accent);

        Set(ImGuiCol.ResizeGrip, new Vector4(0, 0, 0, 0));
        Set(ImGuiCol.ResizeGripHovered, WithAlpha(p.Accent, 0.55f));
        Set(ImGuiCol.ResizeGripActive, WithAlpha(p.Accent, 0.85f));

        // Tabs: inactive tabs recede into the tab strip; the selected tab takes the panel's own tone so it
        // reads as one surface with its content. Which panel has focus is marked by an accent line on its
        // tab (EditorGui.MarkFocusedTab), since this ImGui version has no tab overline of its own.
        Set(ImGuiCol.Tab, p.TabBg);
        Set(ImGuiCol.TabHovered, p.TabHovered);
        Set(ImGuiCol.TabActive, p.TabActive);
        Set(ImGuiCol.TabUnfocused, p.TabBg);
        Set(ImGuiCol.TabUnfocusedActive, p.TabActive);

        Set(ImGuiCol.DockingPreview, WithAlpha(p.Accent, 0.35f));
        Set(ImGuiCol.DockingEmptyBg, p.TitleBg);

        Set(ImGuiCol.PlotLines, p.Accent);
        Set(ImGuiCol.PlotLinesHovered, p.AccentHovered);
        Set(ImGuiCol.PlotHistogram, p.Accent);
        Set(ImGuiCol.PlotHistogramHovered, p.AccentHovered);

        Set(ImGuiCol.TableHeaderBg, p.HeaderBg);
        Set(ImGuiCol.TableBorderStrong, p.Border);
        Set(ImGuiCol.TableBorderLight, p.Separator);
        Set(ImGuiCol.TableRowBg, new Vector4(0, 0, 0, 0));
        Set(ImGuiCol.TableRowBgAlt, WithAlpha(p.Text, 0.025f));

        // Without these the stock dark preset's yellow drop outline and white nav frame leak through.
        Set(ImGuiCol.DragDropTarget, p.AccentHovered);
        Set(ImGuiCol.NavHighlight, p.Accent);
        Set(ImGuiCol.NavWindowingHighlight, WithAlpha(p.Accent, 0.70f));
        Set(ImGuiCol.NavWindowingDimBg, new Vector4(0, 0, 0, 0.35f));
        Set(ImGuiCol.ModalWindowDimBg, new Vector4(0, 0, 0, 0.45f));

        // Metrics.
        style.WindowRounding = Metrics.WindowRounding;
        style.ChildRounding = Metrics.ChildRounding;
        style.FrameRounding = Metrics.FrameRounding;
        style.PopupRounding = Metrics.PopupRounding;
        style.TabRounding = Metrics.TabRounding;
        style.GrabRounding = Metrics.GrabRounding;
        style.ScrollbarRounding = Metrics.ScrollbarRounding;

        style.WindowBorderSize = Metrics.WindowBorderSize;
        style.FrameBorderSize = Metrics.FrameBorderSize;
        style.ChildBorderSize = 0.0f;   // component cards paint their own hairline; avoid doubled lines
        style.PopupBorderSize = Metrics.PopupBorderSize;
        style.TabBorderSize = Metrics.TabBorderSize;
        style.TabBarBorderSize = Metrics.TabBarBorderSize;
        style.DockingSeparatorSize = Metrics.DockingSeparatorSize;
        style.SeparatorTextBorderSize = 1.0f;

        style.WindowPadding = Metrics.WindowPadding;
        style.FramePadding = Metrics.FramePadding;
        style.ItemSpacing = Metrics.ItemSpacing;
        style.ItemInnerSpacing = Metrics.ItemInnerSpacing;
        style.CellPadding = Metrics.CellPadding;

        style.ScrollbarSize = Metrics.ScrollbarSize;
        style.GrabMinSize = Metrics.GrabMinSize;
        style.IndentSpacing = Metrics.IndentSpacing;

        style.WindowTitleAlign = new Vector2(0.0f, 0.5f);
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0.0f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.None;

        // Mirror the log colors onto the engine-owned developer console (the engine does not
        // reference the editor, so the theme pushes these values instead of the console pulling them).
        // The monospaced font is handed over the same way so the console reads like a terminal.
        Spot.Engine.Console.DevConsole.DefaultTextColor = p.LogText;
        Spot.Engine.Console.DevConsole.CommandColor = p.LogCommand;
        Spot.Engine.Console.DevConsole.ErrorColor = p.LogError;
        Spot.Engine.Console.DevConsole.MonospaceFont = EditorFonts.Mono;
        Spot.Engine.Console.DevConsole.LevelIcons = new Dictionary<Spot.Engine.LogLevel, string>
        {
            [Spot.Engine.LogLevel.Trace] = EditorIcons.Bug,
            [Spot.Engine.LogLevel.Info] = EditorIcons.Info,
            [Spot.Engine.LogLevel.Warn] = EditorIcons.Warning,
            [Spot.Engine.LogLevel.Error] = EditorIcons.Error,
        };
    }

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    private static Vector4 Lighten(Vector4 c, float amount) =>
        new(
            Math.Clamp(c.X + amount, 0.0f, 1.0f),
            Math.Clamp(c.Y + amount, 0.0f, 1.0f),
            Math.Clamp(c.Z + amount, 0.0f, 1.0f),
            c.W);
}
