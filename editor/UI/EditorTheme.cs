using System.Numerics;
using ImGuiNET;

namespace Spot.Editor.UI;

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
    public float WindowRounding = 4.0f;
    public float ChildRounding = 4.0f;
    public float FrameRounding = 3.0f;
    public float PopupRounding = 3.0f;
    public float TabRounding = 4.0f;
    public float GrabRounding = 3.0f;
    public float ScrollbarRounding = 3.0f;

    public float WindowBorderSize = 1.0f;
    public float FrameBorderSize = 0.0f;

    public Vector2 WindowPadding = new(10.0f, 10.0f);
    public Vector2 FramePadding = new(8.0f, 5.0f);
    public Vector2 ItemSpacing = new(8.0f, 6.0f);
    public Vector2 ItemInnerSpacing = new(6.0f, 4.0f);

    public float ScrollbarSize = 12.0f;
    public float GrabMinSize = 10.0f;
    public float IndentSpacing = 20.0f;
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
        Set(ImGuiCol.ScrollbarGrabHovered, Lighten(p.ScrollbarGrab, 0.1f));
        Set(ImGuiCol.ScrollbarGrabActive, Lighten(p.ScrollbarGrab, 0.2f));

        Set(ImGuiCol.CheckMark, p.CheckMark);
        Set(ImGuiCol.SliderGrab, p.SliderGrab);
        Set(ImGuiCol.SliderGrabActive, p.AccentActive);

        Set(ImGuiCol.Button, p.Button);
        Set(ImGuiCol.ButtonHovered, p.ButtonHovered);
        Set(ImGuiCol.ButtonActive, p.ButtonActive);

        // Header slots drive selectables, tree nodes and collapsing headers => use the accent.
        Set(ImGuiCol.Header, WithAlpha(p.Accent, 0.55f));
        Set(ImGuiCol.HeaderHovered, WithAlpha(p.Accent, 0.75f));
        Set(ImGuiCol.HeaderActive, p.Accent);

        Set(ImGuiCol.Separator, p.Separator);
        Set(ImGuiCol.SeparatorHovered, WithAlpha(p.Accent, 0.6f));
        Set(ImGuiCol.SeparatorActive, p.Accent);

        Set(ImGuiCol.ResizeGrip, WithAlpha(p.Accent, 0.25f));
        Set(ImGuiCol.ResizeGripHovered, WithAlpha(p.Accent, 0.55f));
        Set(ImGuiCol.ResizeGripActive, WithAlpha(p.Accent, 0.85f));

        Set(ImGuiCol.Tab, p.TabBg);
        Set(ImGuiCol.TabHovered, p.TabHovered);
        Set(ImGuiCol.TabActive, p.TabActive);
        Set(ImGuiCol.TabUnfocused, p.TabBg);
        Set(ImGuiCol.TabUnfocusedActive, p.HeaderBg);

        Set(ImGuiCol.PlotLines, p.Accent);
        Set(ImGuiCol.PlotHistogram, p.Accent);

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

        style.WindowPadding = Metrics.WindowPadding;
        style.FramePadding = Metrics.FramePadding;
        style.ItemSpacing = Metrics.ItemSpacing;
        style.ItemInnerSpacing = Metrics.ItemInnerSpacing;

        style.ScrollbarSize = Metrics.ScrollbarSize;
        style.GrabMinSize = Metrics.GrabMinSize;
        style.IndentSpacing = Metrics.IndentSpacing;

        style.WindowTitleAlign = new Vector2(0.0f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.None;

        // Mirror the log colors onto the engine-owned developer console (the engine does not
        // reference the editor, so the theme pushes these values instead of the console pulling them).
        Spot.Console.DevConsole.DefaultTextColor = p.LogText;
        Spot.Console.DevConsole.CommandColor = p.LogCommand;
        Spot.Console.DevConsole.ErrorColor = p.LogError;
    }

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    private static Vector4 Lighten(Vector4 c, float amount) =>
        new(
            Math.Clamp(c.X + amount, 0.0f, 1.0f),
            Math.Clamp(c.Y + amount, 0.0f, 1.0f),
            Math.Clamp(c.Z + amount, 0.0f, 1.0f),
            c.W);
}
