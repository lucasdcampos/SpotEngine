using System;
using Spot.Rendering;

namespace Spot.Game;

/// <summary>
/// Procedurally generated, anti-aliased white icon textures for the menu — a target reticle for shooter-style
/// games and a gear for the engine test scenes. RGB is white and coverage lives in the alpha channel, so a UI
/// <c>Image</c> tints them with its color and the blended UI pass keeps their edges smooth (unlike the
/// alpha-tested 2D sprite pass). Each is rasterized once, supersampled for clean edges, and cached.
/// </summary>
public static class MenuIcons
{
    private const int Size = 96;
    private const int Super = 4; // supersamples per axis

    private static Texture2D? s_target;
    private static Texture2D? s_gear;

    /// <summary>A reticle/target — used for shooter-style game entries.</summary>
    public static Texture2D Target => s_target ??= Generate(Reticle);

    /// <summary>A gear/cog — used for generic engine test-scene entries.</summary>
    public static Texture2D Gear => s_gear ??= Generate(Cog);

    // A ringed reticle with a center dot and four tick marks along the axes.
    private static bool Reticle(float u, float v)
    {
        float r = MathF.Sqrt(u * u + v * v);
        if (r >= 0.60f && r <= 0.82f) return true;                                    // ring
        if (r <= 0.20f) return true;                                                  // center dot
        if (MathF.Abs(v) <= 0.06f && MathF.Abs(u) >= 0.48f && MathF.Abs(u) <= 0.98f) return true; // left/right ticks
        if (MathF.Abs(u) <= 0.06f && MathF.Abs(v) >= 0.48f && MathF.Abs(v) <= 0.98f) return true; // top/bottom ticks
        return false;
    }

    // A cog: a toothed outer boundary (flat-topped teeth) with a round hole in the middle.
    private static bool Cog(float u, float v)
    {
        float r = MathF.Sqrt(u * u + v * v);
        if (r < 0.26f) return false; // center hole

        const int teeth = 7;
        float phase = MathF.Atan2(v, u) * teeth / MathF.Tau;
        phase -= MathF.Floor(phase);
        float outer = phase < 0.5f ? 0.92f : 0.70f;
        return r <= outer;
    }

    private static Texture2D Generate(Func<float, float, bool> inside)
    {
        byte[] pixels = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < Super; sy++)
                {
                    for (int sx = 0; sx < Super; sx++)
                    {
                        float fx = (x + (sx + 0.5f) / Super) / Size * 2f - 1f;
                        float fy = (y + (sy + 0.5f) / Super) / Size * 2f - 1f;
                        if (inside(fx, fy)) hits++;
                    }
                }

                int idx = (y * Size + x) * 4;
                pixels[idx] = 255;
                pixels[idx + 1] = 255;
                pixels[idx + 2] = 255;
                pixels[idx + 3] = (byte)(255 * hits / (Super * Super));
            }
        }

        return new Texture2D(Size, Size, pixels);
    }
}
