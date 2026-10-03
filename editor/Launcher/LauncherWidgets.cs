using System;
using System.Numerics;
using ImGuiNET;
using Spot.DebugUI.UI;
using Spot.Editor.UI;
using static Spot.Editor.Launcher.LauncherColors;

namespace Spot.Editor.Launcher;

internal enum ButtonKind
{
    /// <summary>Accent-filled: the one action a region most wants.</summary>
    Primary,

    /// <summary>A neutral fill with a hairline edge.</summary>
    Secondary,

    /// <summary>No fill until hovered; for low-emphasis actions.</summary>
    Ghost,
}

/// <summary>
/// The launcher's drawing primitives: buttons, fields and text helpers painted on the window draw list over
/// invisible ImGui items, so they keep ImGui's input handling while looking like the rest of the launcher.
/// Each widget works at the current cursor and advances it like a normal ImGui item.
/// </summary>
internal static class LauncherWidgets
{
    private static readonly uint White = 0xFFFFFFFF;

    // ----- Text ------------------------------------------------------------------------------------------

    public static Vector2 Measure(ImFontPtr font, string text)
    {
        ImGui.PushFont(font);
        Vector2 size = ImGui.CalcTextSize(text);
        ImGui.PopFont();
        return size;
    }

    public static void Text(ImDrawListPtr dl, ImFontPtr font, Vector2 pos, Vector4 color, string text) =>
        dl.AddText(font, font.FontSize, Snap(pos), U32(color), text);

    /// <summary>Cuts the end off a too-long label ("My Long Proj...").</summary>
    public static string EllipsizeEnd(ImFontPtr font, string text, float maxWidth)
    {
        if (Measure(font, text).X <= maxWidth) return text;
        float ellipsis = Measure(font, "...").X;
        for (int len = text.Length - 1; len > 0; len--)
        {
            string candidate = text[..len].TrimEnd();
            if (Measure(font, candidate).X + ellipsis <= maxWidth) return candidate + "...";
        }
        return "...";
    }

    /// <summary>Cuts the start off a too-long path ("...\Projects\MyGame"), keeping its meaningful tail.</summary>
    public static string EllipsizeStart(ImFontPtr font, string text, float maxWidth)
    {
        if (Measure(font, text).X <= maxWidth) return text;
        float ellipsis = Measure(font, "...").X;
        for (int start = 1; start < text.Length; start++)
        {
            string candidate = text[start..];
            if (Measure(font, candidate).X + ellipsis <= maxWidth) return "..." + candidate;
        }
        return "...";
    }

    /// <summary>
    /// Shortens a path in the middle ("C:\Users\...\Spot\MyGame"): the root and first folder say where it lives,
    /// the tail which project it is. The tail starts at a folder boundary whenever one fits.
    /// </summary>
    public static string EllipsizePath(ImFontPtr font, string path, float maxWidth)
    {
        if (Measure(font, path).X <= maxWidth) return path;

        int headEnd = path.IndexOfAny(PathSeparators, Math.Min(path.Length, 3));
        if (headEnd < 0) return EllipsizeEnd(font, path, maxWidth);
        string head = path[..(headEnd + 1)] + "...";
        float budget = maxWidth - Measure(font, head).X;

        // Walk the folder boundaries left to right; the first tail that fits is the longest one.
        for (int i = headEnd + 1; budget > 0 && i < path.Length; i++)
        {
            if (Array.IndexOf(PathSeparators, path[i]) < 0) continue;
            string tail = path[i..];
            if (Measure(font, tail).X <= budget) return head + tail;
        }

        // Even the last folder is too long: keep where it lives instead (its name is on the card already).
        return EllipsizeEnd(font, path, maxWidth);
    }

    private static readonly char[] PathSeparators = { '\\', '/' };

    /// <summary>Shows a tooltip for the last item once the mouse rests on it.</summary>
    public static void Tooltip(string text)
    {
        // The binding forwards the string as a printf format, so escape any '%' a path might contain.
        ImGui.SetItemTooltip(text.Replace("%", "%%"));
    }

    // ----- Buttons ---------------------------------------------------------------------------------------

    /// <summary>A button with an optional leading glyph, its content centered. Returns true when clicked.</summary>
    public static bool Button(string id, string? glyph, string label, Vector2 size, ButtonKind kind,
        in LauncherColors c, string? tooltip = null, bool enabled = true)
    {
        Vector2 min = ImGui.GetCursorScreenPos();
        Vector2 max = min + size;
        if (!enabled) ImGui.BeginDisabled();
        bool clicked = ImGui.InvisibleButton(id, size);
        if (!enabled) ImGui.EndDisabled();
        bool hovered = enabled && ImGui.IsItemHovered();
        bool held = enabled && ImGui.IsItemActive();
        if (tooltip != null) Tooltip(tooltip);

        var dl = ImGui.GetWindowDrawList();
        Vector4 fg;
        switch (enabled ? kind : ButtonKind.Secondary)
        {
            case ButtonKind.Primary:
                dl.AddRectFilled(min, max, U32(held ? c.AccentActive : hovered ? c.AccentHovered : c.Accent),
                    LauncherLayout.ControlRounding);
                // A faint top light gives the filled button a little body without a visible gradient.
                dl.AddLine(min + new Vector2(LauncherLayout.ControlRounding, 0.5f),
                    new Vector2(max.X - LauncherLayout.ControlRounding, min.Y + 0.5f),
                    U32(WithAlpha(c.OnAccent, held ? 0.0f : 0.14f)));
                fg = c.OnAccent;
                break;
            case ButtonKind.Secondary:
                dl.AddRectFilled(min, max, U32(held ? c.FillActive : hovered ? c.FillHovered : c.Fill),
                    LauncherLayout.ControlRounding);
                dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), U32(c.Hairline), LauncherLayout.ControlRounding);
                fg = c.Text;
                break;
            default:
                if (hovered || held)
                {
                    dl.AddRectFilled(min, max, U32(held ? c.FillActive : c.Fill), LauncherLayout.ControlRounding);
                }
                fg = hovered ? c.Text : c.TextMuted;
                break;
        }
        if (!enabled) fg = c.TextFaint; // disabled buttons of every kind go neutral, so none reads as clickable

        ImFontPtr font = EditorFonts.Body;
        Vector2 labelSize = label.Length > 0 ? Measure(font, label) : new Vector2(0, font.FontSize);
        float glyphWidth = glyph != null ? Measure(font, glyph).X + (label.Length > 0 ? Space.Sm : 0.0f) : 0.0f;
        float x = min.X + (size.X - glyphWidth - labelSize.X) * 0.5f;
        float y = min.Y + (size.Y - labelSize.Y) * 0.5f;
        if (glyph != null) Text(dl, font, new Vector2(x, y), WithAlpha(fg, fg.W * 0.92f), glyph);
        Text(dl, font, new Vector2(x + glyphWidth, y), fg, label);

        return clicked && enabled;
    }

    /// <summary>A square, glyph-only button. <paramref name="highlighted"/> keeps it lit (e.g. while its menu is open).</summary>
    public static bool IconButton(string id, string glyph, Vector2 size, in LauncherColors c, string? tooltip,
        bool highlighted = false)
    {
        Vector2 min = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton(id, size);
        bool hovered = ImGui.IsItemHovered();
        bool held = ImGui.IsItemActive();
        if (tooltip != null) Tooltip(tooltip);

        var dl = ImGui.GetWindowDrawList();
        if (hovered || held || highlighted)
        {
            dl.AddRectFilled(min, min + size, U32(held ? c.FillActive : c.FillHovered), LauncherLayout.ControlRounding - 1);
        }
        Vector2 gs = Measure(EditorFonts.Body, glyph);
        Text(dl, EditorFonts.Body, min + (size - gs) * 0.5f, hovered || highlighted ? c.Text : c.TextMuted, glyph);
        return clicked;
    }

    /// <summary>The width <see cref="DropdownButton"/> takes for this content, for laying out around it.</summary>
    public static float DropdownWidth(string glyph, string label)
    {
        ImFontPtr font = EditorFonts.Body;
        return Space.Md + Measure(font, glyph).X + Space.Sm + Measure(font, label).X + Space.Sm +
            Measure(font, EditorIcons.CaretDown).X + Space.Md;
    }

    /// <summary>
    /// A compact drop-down trigger (glyph, label, caret) styled like the header's other controls. Returns true when
    /// clicked; the caller opens its popup.
    /// </summary>
    public static bool DropdownButton(string id, string glyph, string label, in LauncherColors c, out Vector2 bottomLeft)
    {
        ImFontPtr font = EditorFonts.Body;
        float pad = Space.Md;
        float glyphW = Measure(font, glyph).X;
        float caretW = Measure(font, EditorIcons.CaretDown).X;
        var size = new Vector2(DropdownWidth(glyph, label), LauncherLayout.ControlHeight);

        Vector2 min = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton(id, size);
        bool hovered = ImGui.IsItemHovered();
        bottomLeft = new Vector2(min.X, min.Y + size.Y);

        var dl = ImGui.GetWindowDrawList();
        DrawField(dl, min, min + size, hovered, false, c);
        float y = min.Y + (size.Y - Measure(font, label).Y) * 0.5f;
        Text(dl, font, new Vector2(min.X + pad, y), c.TextMuted, glyph);
        Text(dl, font, new Vector2(min.X + pad + glyphW + Space.Sm, y), c.Text, label);
        Text(dl, font, new Vector2(min.X + size.X - pad - caretW, y), c.TextMuted, EditorIcons.CaretDown);
        return clicked;
    }

    /// <summary>
    /// A row of glyph segments sharing one frame, one of them selected (e.g. grid/list view). Returns the selected
    /// index, which changes on click.
    /// </summary>
    public static int Segmented(string id, string[] glyphs, string[] tooltips, int selected, in LauncherColors c)
    {
        float h = LauncherLayout.ControlHeight;
        float segment = h + Space.Xs;
        Vector2 min = ImGui.GetCursorScreenPos();
        Vector2 max = min + new Vector2(segment * glyphs.Length, h);
        var dl = ImGui.GetWindowDrawList();
        DrawField(dl, min, max, false, false, c);

        ImGui.PushID(id);
        int result = selected;
        for (int i = 0; i < glyphs.Length; i++)
        {
            Vector2 sMin = min + new Vector2(segment * i, 0);
            ImGui.SetCursorScreenPos(sMin);
            if (ImGui.InvisibleButton($"##seg{i}", new Vector2(segment, h))) result = i;
            bool hovered = ImGui.IsItemHovered();
            Tooltip(tooltips[i]);

            const float inset = 3.0f;
            if (i == selected)
            {
                dl.AddRectFilled(sMin + new Vector2(inset), sMin + new Vector2(segment, h) - new Vector2(inset),
                    U32(c.FillHovered), LauncherLayout.ControlRounding - 2);
            }
            Vector2 gs = Measure(EditorFonts.Body, glyphs[i]);
            Text(dl, EditorFonts.Body, sMin + (new Vector2(segment, h) - gs) * 0.5f,
                i == selected || hovered ? c.Text : c.TextMuted, glyphs[i]);
        }
        ImGui.PopID();
        ImGui.SetCursorScreenPos(new Vector2(max.X, min.Y));
        ImGui.Dummy(Vector2.Zero);
        return result;
    }

    /// <summary>
    /// A search box with a leading magnifier and a clear button. Escape clears it. Set
    /// <paramref name="takeFocus"/> to move keyboard focus into it (Ctrl+F).
    /// </summary>
    public static void SearchField(string id, string hint, ref string text, float width, in LauncherColors c, bool takeFocus)
    {
        float h = LauncherLayout.ControlHeight;
        Vector2 min = ImGui.GetCursorScreenPos();
        Vector2 max = min + new Vector2(width, h);
        var dl = ImGui.GetWindowDrawList();

        // The input is transparent and starts past the glyph; its frame depends on the input's hover/focus, so it
        // is painted afterwards on a channel underneath.
        float glyphW = Measure(EditorFonts.Body, EditorIcons.Search).X;
        float textLeft = Space.Md + glyphW + Space.Sm;
        float clearW = text.Length > 0 ? h : 0.0f;

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, c.TextFaint);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, (h - ImGui.GetFontSize()) * 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f);
        ImGui.SetCursorScreenPos(min + new Vector2(textLeft, 0));
        ImGui.SetNextItemWidth(width - textLeft - clearW - Space.Xs);
        if (takeFocus) ImGui.SetKeyboardFocusHere();
        ImGui.InputTextWithHint(id, hint, ref text, 128, ImGuiInputTextFlags.EscapeClearsAll);
        bool active = ImGui.IsItemActive();
        bool hovered = ImGui.IsItemHovered();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);

        dl.ChannelsSetCurrent(0);
        DrawField(dl, min, max, hovered, active, c);
        dl.ChannelsMerge();
        Text(dl, EditorFonts.Body, new Vector2(min.X + Space.Md, min.Y + (h - ImGui.GetFontSize()) * 0.5f),
            active ? c.Text : c.TextMuted, EditorIcons.Search);

        if (clearW > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(max.X - clearW, min.Y));
            if (IconButton(id + "_clear", EditorIcons.Times, new Vector2(clearW, h), c, "Clear search (Esc)"))
            {
                text = string.Empty;
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(max.X, min.Y));
        ImGui.Dummy(Vector2.Zero);
    }

    // The shared look of header controls: an inset fill with a hairline edge, accented while focused.
    private static void DrawField(ImDrawListPtr dl, Vector2 min, Vector2 max, bool hovered, bool focused, in LauncherColors c)
    {
        dl.AddRectFilled(min, max, U32(hovered && !focused ? c.FieldHovered : c.Field), LauncherLayout.ControlRounding);
        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f),
            U32(focused ? WithAlpha(c.Accent, 0.85f) : c.Hairline), LauncherLayout.ControlRounding);
    }

    /// <summary>
    /// A text link row (glyph, label, trailing "opens elsewhere" mark). Its hover fill bleeds a little past the
    /// column edge so the glyph stays aligned with the buttons above. Returns true when clicked.
    /// </summary>
    public static bool LinkRow(string id, string glyph, string label, float width, in LauncherColors c, string? tooltip)
    {
        const float height = 30.0f;
        const float bleed = Space.Sm;
        Vector2 min = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton(id, new Vector2(width, height));
        bool hovered = ImGui.IsItemHovered();
        if (tooltip != null) Tooltip(tooltip);
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var dl = ImGui.GetWindowDrawList();
        if (hovered)
        {
            dl.AddRectFilled(min - new Vector2(bleed, 0), min + new Vector2(width + bleed, height), U32(c.Fill),
                LauncherLayout.ControlRounding);
        }

        ImFontPtr font = EditorFonts.Body;
        float y = min.Y + (height - font.FontSize) * 0.5f;
        Vector4 fg = hovered ? c.Text : c.TextMuted;
        float glyphSlot = font.FontSize + Space.Sm + 2.0f;
        Vector2 gs = Measure(font, glyph);
        Text(dl, font, new Vector2(min.X + (font.FontSize - gs.X) * 0.5f, y), fg, glyph);
        Text(dl, font, new Vector2(min.X + glyphSlot, y), fg, label);

        // The "opens in your browser" mark appears on hover only, so the idle footer stays quiet.
        if (hovered)
        {
            float markSize = font.FontSize * 0.75f;
            dl.AddText(font, markSize, Snap(new Vector2(min.X + width - markSize, min.Y + (height - markSize) * 0.5f)),
                U32(c.TextMuted), EditorIcons.ExternalLink);
        }
        return clicked;
    }

    // ----- Menus -----------------------------------------------------------------------------------------

    /// <summary>Roomier padding and rounding for the launcher's drop-down menus; pair with <see cref="PopMenuStyle"/>.</summary>
    public static void PushMenuStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Space.Sm, Space.Sm));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(Space.Md, Space.Sm));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, LauncherLayout.ControlRounding);
    }

    public static void PopMenuStyle() => ImGui.PopStyleVar(3);

    // ----- Shapes ----------------------------------------------------------------------------------------

    /// <summary>Draws a rounded "chip" around <paramref name="text"/> and returns its width.</summary>
    public static float Pill(ImDrawListPtr dl, Vector2 pos, ImFontPtr font, string text, Vector4 bg, Vector4 fg)
    {
        const float padX = 7.0f, padY = 2.0f;
        Vector2 ts = Measure(font, text);
        Vector2 max = pos + new Vector2(ts.X + padX * 2, ts.Y + padY * 2);
        dl.AddRectFilled(pos, max, U32(bg), (max.Y - pos.Y) * 0.5f);
        Text(dl, font, pos + new Vector2(padX, padY), fg, text);
        return max.X - pos.X;
    }

    /// <summary>A rounded rectangle with a vertical gradient.</summary>
    public static void GradientRect(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 top, Vector4 bottom,
        float rounding, ImDrawFlags corners)
    {
        int start = dl.VtxBuffer.Size;
        dl.AddRectFilled(min, max, White, rounding, corners);
        AssetIcons.ApplyGradient(dl, start, min, new Vector2(min.X, max.Y), top, bottom);
    }

    /// <summary>Text drawn at fractional positions blurs; glyph quads snap to whole pixels.</summary>
    public static Vector2 Snap(Vector2 p) => new(MathF.Round(p.X), MathF.Round(p.Y));
}
