using System;
using System.Numerics;
using ImGuiNET;
using Spot.Rendering;

namespace Spot.Editor.UI;

/// <summary>
/// Draws a full-window "loading" cover (solid background, animated spinner and a title/subtitle).
/// Owns no state, so it can be presented both by the launcher (to hide the freeze while the editor
/// scene is constructed) and by the editor itself (to hide the first few frames while the dock
/// layout and framebuffers settle). Sharing one renderer keeps the hand-off between the two visually
/// seamless.
/// </summary>
public static class LoadingScreen
{
    /// <summary>
    /// Covers the whole window with the loading screen for the current frame. Call this from a
    /// scene's <c>OnImGuiRender</c> (before ImGui's draw data is rendered).
    /// </summary>
    public static void Present(EditorPalette palette, string title, string subtitle)
    {
        // Backstop: clear the entire framebuffer to the loading background before drawing the ImGui
        // cover. Right after a window resize (e.g. the launcher's compact window growing into the
        // editor) ImGui's DisplaySize can lag the real framebuffer by a frame, which would leave the
        // uncovered area flashing black. glClear ignores the (possibly stale) ImGui viewport and
        // fills the whole framebuffer, so the transition always stays a solid loading screen.
        Renderer.SetClearColor(palette.WindowBg.X, palette.WindowBg.Y, palette.WindowBg.Z, 1.0f);
        Renderer.Clear();

        var vp = ImGui.GetMainViewport();
        Draw(ImGui.GetForegroundDrawList(), vp.Pos, vp.Size, palette, title, subtitle);
    }

    /// <summary>
    /// Paints the loading cover over the region (<paramref name="pos"/>, <paramref name="size"/>).
    /// Pass the foreground draw list to cover everything, or a window draw list to cover one window.
    /// </summary>
    public static void Draw(ImDrawListPtr drawList, Vector2 pos, Vector2 size, EditorPalette palette, string title, string subtitle)
    {
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(palette.WindowBg));

        Vector2 center = pos + size * 0.5f;
        float time = (float)ImGui.GetTime();

        DrawSpinner(drawList, center - new Vector2(0, 48), 22.0f, palette.Accent, time);

        if (!string.IsNullOrEmpty(title))
        {
            const float titleSize = 22.0f;
            float scale = titleSize / ImGui.GetFontSize();
            Vector2 ts = ImGui.CalcTextSize(title) * scale;
            drawList.AddText(ImGui.GetFont(), titleSize,
                new Vector2(center.X - ts.X * 0.5f, center.Y + 4.0f),
                ImGui.GetColorU32(palette.Text), title);
        }

        if (!string.IsNullOrEmpty(subtitle))
        {
            Vector2 ss = ImGui.CalcTextSize(subtitle);
            drawList.AddText(
                new Vector2(center.X - ss.X * 0.5f, center.Y + 4.0f + 30.0f),
                ImGui.GetColorU32(palette.TextDisabled), subtitle);
        }
    }

    // A ring of dots whose size/opacity trail off around the circle, producing a smooth "comet" spin.
    private static void DrawSpinner(ImDrawListPtr drawList, Vector2 center, float radius, Vector4 accent, float time)
    {
        const int count = 12;
        const float speed = 6.0f;
        for (int i = 0; i < count; i++)
        {
            float frac = i / (float)count;
            float angle = -time * speed + frac * MathF.PI * 2.0f;
            Vector2 p = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            float alpha = 0.15f + 0.85f * frac;
            float dot = 2.0f + 2.5f * frac;
            drawList.AddCircleFilled(p, dot, ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, alpha)));
        }
    }
}
