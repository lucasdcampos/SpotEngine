using System.Numerics;
using Spot.Engine.Graphics;

namespace ProvingGrounds;

/// <summary>
/// The effects' textures, generated at startup: a soft streak for tracers and sparks, a star for muzzle flashes, a
/// bullet hole and a shockwave ring. White with the shape in alpha (the hole also darkens toward its core), so the
/// draw color tints them.
/// </summary>
internal sealed class FxTextures : IDisposable
{
    public Texture2D Streak { get; } = Generate(64, 16, (u, v) =>
    {
        // Bright along the middle, soft across, tapering toward both ends.
        float across = MathF.Exp(-v * v * 9.0f);
        float along = MathF.Pow(MathF.Max(0.0f, 1.0f - u * u), 0.6f);
        return across * along;
    });

    public Texture2D Flash { get; } = Generate(128, 128, (u, v) =>
    {
        float r = MathF.Sqrt(u * u + v * v);
        float angle = MathF.Atan2(v, u);
        float spikes = MathF.Pow(MathF.Abs(MathF.Cos(angle * 3.0f)), 18.0f) * MathF.Max(0.0f, 1.0f - r);
        float core = MathF.Exp(-r * r * 14.0f);
        float glow = MathF.Exp(-r * r * 3.5f) * 0.35f;
        return MathF.Min(1.0f, core + spikes * 0.9f + glow);
    });

    public Texture2D Hole { get; } = Generate(64, 64, (u, v) =>
    {
        float r = MathF.Sqrt(u * u + v * v);
        float core = r < 0.22f ? 1.0f : MathF.Max(0.0f, 1.0f - (r - 0.22f) / 0.08f);
        float scorch = MathF.Max(0.0f, 1.0f - r) * 0.45f;
        return MathF.Min(1.0f, MathF.Max(core * 0.95f, scorch));
    });

    public Texture2D Ring { get; } = Generate(128, 128, (u, v) =>
    {
        float r = MathF.Sqrt(u * u + v * v);
        float d = (r - 0.82f) / 0.1f;
        return MathF.Exp(-d * d) * (r < 1.0f ? 1.0f : 0.0f);
    });

    public void Dispose()
    {
        Streak.Dispose();
        Flash.Dispose();
        Hole.Dispose();
        Ring.Dispose();
    }

    // Fills a white texture whose alpha comes from a function of centered coordinates u, v in -1..1.
    private static Texture2D Generate(int width, int height, Func<float, float, float> alpha)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width * 2.0f - 1.0f;
                float v = (y + 0.5f) / height * 2.0f - 1.0f;
                int i = (y * width + x) * 4;
                pixels[i] = 255;
                pixels[i + 1] = 255;
                pixels[i + 2] = 255;
                pixels[i + 3] = (byte)MathF.Round(Math.Clamp(alpha(u, v), 0.0f, 1.0f) * 255.0f);
            }
        }

        return new Texture2D((uint)width, (uint)height, pixels);
    }
}
