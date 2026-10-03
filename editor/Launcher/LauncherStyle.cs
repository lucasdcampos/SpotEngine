using System;
using System.Numerics;
using ImGuiNET;
using Spot.DebugUI.UI;

namespace Spot.Editor.Launcher;

/// <summary>The launcher's spacing scale. Every padding and gap is one of these steps, so edges line up.</summary>
internal static class Space
{
    public const float Xs = 4.0f;
    public const float Sm = 8.0f;
    public const float Md = 12.0f;
    public const float Lg = 16.0f;
    public const float Xl = 24.0f;
    public const float Xxl = 32.0f;
}

/// <summary>Fixed geometry of the launcher's layout and controls.</summary>
internal static class LauncherLayout
{
    public const float SidebarWidth = 232.0f;
    public const float SidebarPaddingX = Space.Xl;
    public const float MainPaddingX = Space.Xxl;

    /// <summary>Top inset of both columns, so the brand mark and the page title share one row.</summary>
    public const float TopInset = Space.Xl + Space.Xs;
    public const float BottomInset = Space.Xl;

    /// <summary>The brand mark and the header row are this tall; header controls center on it.</summary>
    public const float HeaderHeight = 36.0f;
    public const float ButtonHeight = 36.0f;
    public const float ControlHeight = 30.0f;

    public const float ControlRounding = 6.0f;
    public const float CardRounding = 8.0f;

    public const float TileMinWidth = 184.0f;
    public const float TileGap = Space.Lg;
    public const float TilePadding = Space.Md;

    public const float RowHeight = 68.0f;
    public const float RowGap = Space.Sm;
    public const float RowThumbnailWidth = 96.0f;

    /// <summary>Thumbnails (and their placeholders) are always 16:9, like the captured viewport.</summary>
    public const float ThumbnailAspect = 16.0f / 9.0f;
}

/// <summary>
/// The launcher's colors, derived from the active editor palette rather than added to it: each surface is a
/// small step from the palette's own tones, so any theme (light ones included) gets a matching launcher.
/// </summary>
internal readonly struct LauncherColors
{
    public Vector4 Sidebar { get; init; }
    public Vector4 Main { get; init; }

    public Vector4 Card { get; init; }
    public Vector4 CardHovered { get; init; }
    public Vector4 CardSelected { get; init; }
    public Vector4 CardBorder { get; init; }
    public Vector4 CardBorderHovered { get; init; }

    public Vector4 Text { get; init; }
    public Vector4 TextMuted { get; init; }
    public Vector4 TextFaint { get; init; }

    public Vector4 Accent { get; init; }
    public Vector4 AccentHovered { get; init; }
    public Vector4 AccentActive { get; init; }
    public Vector4 OnAccent { get; init; }

    /// <summary>Neutral fills for secondary buttons and toggles, drawn over either column.</summary>
    public Vector4 Fill { get; init; }
    public Vector4 FillHovered { get; init; }
    public Vector4 FillActive { get; init; }
    public Vector4 Hairline { get; init; }

    public Vector4 Field { get; init; }
    public Vector4 FieldHovered { get; init; }

    public Vector4 Danger { get; init; }
    public Vector4 Warning { get; init; }

    public static LauncherColors From(EditorPalette p) => new()
    {
        Sidebar = p.HeaderBg,
        Main = p.WindowBg,

        Card = Mix(p.WindowBg, p.Text, 0.035f),
        CardHovered = Mix(p.WindowBg, p.Text, 0.07f),
        CardSelected = Mix(Mix(p.WindowBg, p.Text, 0.035f), p.Accent, 0.12f),
        CardBorder = WithAlpha(p.Text, 0.06f),
        CardBorderHovered = WithAlpha(p.Text, 0.16f),

        Text = p.Text,
        TextMuted = p.TextDisabled,
        TextFaint = Mix(p.TextDisabled, p.WindowBg, 0.3f),

        Accent = p.Accent,
        AccentHovered = p.AccentHovered,
        AccentActive = p.AccentActive,
        OnAccent = new Vector4(1, 1, 1, 1),

        Fill = WithAlpha(p.Text, 0.06f),
        FillHovered = WithAlpha(p.Text, 0.10f),
        FillActive = WithAlpha(p.Text, 0.04f),
        Hairline = WithAlpha(p.Text, 0.08f),

        Field = p.FrameBg,
        FieldHovered = p.FrameBgHovered,

        Danger = p.LogError,
        Warning = new Vector4(0.88f, 0.66f, 0.28f, 1.0f),
    };

    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    /// <summary>Blends <paramref name="a"/> toward <paramref name="b"/>, keeping <paramref name="a"/>'s alpha.</summary>
    public static Vector4 Mix(Vector4 a, Vector4 b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W);

    public static uint U32(Vector4 c) => ImGui.GetColorU32(c);

    /// <summary>A color from hue/saturation/value, each in [0, 1].</summary>
    public static Vector4 Hsv(float h, float s, float v)
    {
        ImGui.ColorConvertHSVtoRGB(h - MathF.Floor(h), s, v, out float r, out float g, out float b);
        return new Vector4(r, g, b, 1.0f);
    }
}
