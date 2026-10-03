using System;
using System.Numerics;
using ImGuiNET;
using static Spot.Editor.Launcher.LauncherColors;

namespace Spot.Editor.Launcher;

/// <summary>
/// The picture a project card shows until the editor has captured a real thumbnail: a new scene's default cube
/// on a perspective floor grid, under a tinted sky with a haze at the horizon. The tint is derived from the
/// project name, so each project keeps a recognizable color from one launch to the next.
/// </summary>
internal static class ProjectPlaceholder
{
    /// <param name="saturation">0 draws the neutral version used by the empty state.</param>
    public static void Draw(ImDrawListPtr dl, string seed, Vector2 min, Vector2 max, float rounding, ImDrawFlags corners,
        float saturation = 1.0f)
    {
        Vector2 size = max - min;
        float hue = Hue(seed);
        LauncherWidgets.GradientRect(dl, min, max,
            Hsv(hue, 0.30f * saturation, 0.31f), Hsv(hue + 0.03f, 0.40f * saturation, 0.14f), rounding, corners);

        dl.PushClipRect(min, max, true);

        // The floor: lines converging on a vanishing point at the horizon, rungs spaced by perspective.
        float horizon = min.Y + size.Y * 0.60f;
        float vanishX = min.X + size.X * 0.5f;
        uint line = U32(new Vector4(1, 1, 1, 0.06f));
        const int rays = 8;
        for (int i = -rays; i <= rays; i++)
        {
            dl.AddLine(new Vector2(vanishX + i * size.X * 0.04f, horizon),
                new Vector2(vanishX + i * size.X * 0.24f, max.Y), line);
        }
        const int rungs = 5;
        for (int k = 1; k <= rungs; k++)
        {
            float t = k / (float)rungs;
            float y = horizon + (max.Y - horizon) * t * t;
            dl.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), line);
        }

        // A haze band where floor meets sky.
        uint clear = U32(new Vector4(1, 1, 1, 0.0f));
        uint haze = U32(new Vector4(1, 1, 1, 0.07f));
        dl.AddRectFilledMultiColor(new Vector2(min.X, horizon - size.Y * 0.22f), new Vector2(max.X, horizon),
            clear, clear, haze, haze);
        dl.AddRectFilledMultiColor(new Vector2(min.X, horizon), new Vector2(max.X, horizon + size.Y * 0.10f),
            haze, haze, clear, clear);

        dl.PopClipRect();

        DrawCube(dl, new Vector2(vanishX, horizon + (max.Y - horizon) * 0.42f), size.Y * 0.15f);
    }

    // A lit isometric cube standing on its bottom vertex at `ground`, with a soft contact shadow.
    private static void DrawCube(ImDrawListPtr dl, Vector2 ground, float e)
    {
        for (int i = 0; i < 3; i++)
        {
            float spread = 1.0f + i * 0.25f;
            dl.AddEllipseFilled(ground + new Vector2(0, e * 0.06f), new Vector2(e * 1.05f * spread, e * 0.26f * spread),
                U32(new Vector4(0, 0, 0, 0.12f)));
        }

        Vector2 o = ground - new Vector2(0, e);
        float dx = e * 0.866f, dy = e * 0.5f;
        Vector2 top = o - new Vector2(0, e);
        Vector2 upperRight = o + new Vector2(dx, -dy), lowerRight = o + new Vector2(dx, dy);
        Vector2 upperLeft = o + new Vector2(-dx, -dy), lowerLeft = o + new Vector2(-dx, dy);
        Vector2 bottom = o + new Vector2(0, e);

        // Points go clockwise on screen, which ImGui's anti-aliased fill expects.
        dl.AddQuadFilled(top, upperRight, o, upperLeft, U32(new Vector4(1, 1, 1, 0.36f)));
        dl.AddQuadFilled(upperLeft, o, bottom, lowerLeft, U32(new Vector4(1, 1, 1, 0.20f)));
        dl.AddQuadFilled(o, upperRight, lowerRight, bottom, U32(new Vector4(1, 1, 1, 0.11f)));
        dl.AddLine(upperLeft, o, U32(new Vector4(1, 1, 1, 0.18f)));
        dl.AddLine(o, upperRight, U32(new Vector4(1, 1, 1, 0.18f)));
    }

    // A stable hue per name (FNV-1a), unlike string.GetHashCode, which changes every run.
    private static float Hue(string seed)
    {
        uint hash = 2166136261;
        foreach (char ch in seed.ToUpperInvariant())
        {
            hash ^= ch;
            hash *= 16777619;
        }
        return hash % 360 / 360.0f;
    }
}
