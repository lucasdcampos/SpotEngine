using System.Numerics;
using Spot.Engine.Graphics;

namespace ProvingGrounds;

/// <summary>The icons <see cref="UITextures.Icon"/> draws.</summary>
internal enum IconShape
{
    Rifle,
    Launcher,
    Infinity,
    Diamond,
    Bolt,
}

/// <summary>
/// The HUD's shapes, generated at startup from signed distances — rounded panels with their outline and shadow, discs,
/// rings, the hit marker, a screen-edge vignette and small icons — so the interface needs no image files. White with
/// the shape in alpha, so a draw color tints them.
/// </summary>
internal static class UITextures
{
    /// <summary>A rounded rectangle, for nine-slicing with a border of <paramref name="radius"/>.</summary>
    public static Texture2D RoundedRect(int radius) =>
        Generate(radius * 2 + 4, radius * 2 + 4, (x, y, w, h) => Coverage(RoundedDistance(x, y, w, h, radius)));

    /// <summary>A one-pixel outline of <see cref="RoundedRect"/>.</summary>
    public static Texture2D RoundedOutline(int radius) =>
        Generate(radius * 2 + 4, radius * 2 + 4, (x, y, w, h) =>
        {
            float d = RoundedDistance(x, y, w, h, radius);
            return Coverage(d) - Coverage(d + 1.0f);
        });

    /// <summary>A soft drop shadow for a rounded rectangle; nine-slice it with a border of radius + blur.</summary>
    public static Texture2D RoundedShadow(int radius, int blur) =>
        Generate((radius + blur) * 2 + 4, (radius + blur) * 2 + 4, (x, y, w, h) =>
        {
            float d = RoundedDistance(x, y, w, h, radius + blur) + blur;
            float t = Math.Clamp((d + blur) / (2.0f * blur), 0.0f, 1.0f);
            float s = 1.0f - t * t * (3.0f - 2.0f * t);
            return s * s;
        });

    /// <summary>A filled, antialiased disc.</summary>
    public static Texture2D Disc(int size = 64) =>
        Generate(size, size, (x, y, w, h) => Coverage(Vector2.Distance(new Vector2(x, y), new Vector2(w, h) * 0.5f) - w * 0.5f + 1.0f));

    /// <summary>A circle outline <paramref name="stroke"/> pixels wide.</summary>
    public static Texture2D Ring(int size = 128, float stroke = 3.0f) =>
        Generate(size, size, (x, y, w, h) =>
        {
            float d = MathF.Abs(Vector2.Distance(new Vector2(x, y), new Vector2(w, h) * 0.5f) - (w * 0.5f - stroke - 1.0f));
            return Coverage(d - stroke * 0.5f);
        });

    /// <summary>A soft round glow, brightest in the middle.</summary>
    public static Texture2D Glow(int size = 64) =>
        Generate(size, size, (x, y, w, h) =>
        {
            float r = Vector2.Distance(new Vector2(x, y), new Vector2(w, h) * 0.5f) / (w * 0.5f);
            return MathF.Exp(-r * r * 4.0f) * (1.0f - MathF.Min(1.0f, r));
        });

    /// <summary>Four short diagonal strokes around an empty middle: the hit marker.</summary>
    public static Texture2D HitMarker(int size = 64) =>
        Generate(size, size, (x, y, w, h) =>
        {
            var p = new Vector2(MathF.Abs(x / w * 2.0f - 1.0f), MathF.Abs(y / h * 2.0f - 1.0f));
            return Coverage(Segment(p, new Vector2(0.38f, 0.38f), new Vector2(0.88f, 0.88f), 0.075f) * w * 0.5f);
        });

    /// <summary>Dark toward the edges, clear in the middle: tinted, it frames the screen.</summary>
    public static Texture2D Vignette(int size = 128) =>
        Generate(size, size, (x, y, w, h) =>
        {
            var p = new Vector2(x / w * 2.0f - 1.0f, y / h * 2.0f - 1.0f);
            float r = MathF.Pow(MathF.Pow(MathF.Abs(p.X), 3.0f) + MathF.Pow(MathF.Abs(p.Y), 3.0f), 1.0f / 3.0f);
            float t = Math.Clamp((r - 0.55f) / 0.5f, 0.0f, 1.0f);
            return t * t;
        });

    /// <summary>A small icon, drawn from signed distances in a -1..1 square (y down) so it stays smooth at any size.</summary>
    public static Texture2D Icon(IconShape shape, int width = 128, int height = 64) =>
        Generate(width, height, (x, y, w, h) =>
        {
            // Square units on the short side, so a wide icon keeps its proportions.
            float unit = MathF.Min(w, h) * 0.5f;
            var p = new Vector2((x - w * 0.5f) / unit, (y - h * 0.5f) / unit);
            return Coverage(IconDistance(shape, p) * unit);
        });

    private static float IconDistance(IconShape shape, Vector2 p) => shape switch
    {
        // A rifle in profile, muzzle to the right: barrel, body, magazine, grip, stock, sight.
        IconShape.Rifle => Union(
            Box(p, new Vector2(1.45f, -0.08f), new Vector2(0.4f, 0.05f), 0.02f),
            Box(p, new Vector2(0.4f, 0.0f), new Vector2(0.75f, 0.17f), 0.04f),
            Box(p, new Vector2(0.25f, 0.42f), new Vector2(0.11f, 0.3f), 0.03f),
            Box(p, new Vector2(-0.3f, 0.36f), new Vector2(0.09f, 0.22f), 0.03f),
            Box(p, new Vector2(-1.15f, 0.06f), new Vector2(0.4f, 0.14f), 0.05f),
            Box(p, new Vector2(0.35f, -0.28f), new Vector2(0.18f, 0.08f), 0.02f)),
        // A stubby launcher: wide barrel, a drum, grip and stock.
        IconShape.Launcher => Union(
            Box(p, new Vector2(1.05f, -0.1f), new Vector2(0.65f, 0.17f), 0.04f),
            Circle(p, new Vector2(0.15f, 0.04f), 0.36f),
            Box(p, new Vector2(-0.4f, 0.0f), new Vector2(0.45f, 0.17f), 0.04f),
            Box(p, new Vector2(-0.45f, 0.38f), new Vector2(0.1f, 0.25f), 0.03f),
            Box(p, new Vector2(-1.15f, 0.05f), new Vector2(0.32f, 0.13f), 0.05f)),
        // Two loops crossing: infinite reserve.
        IconShape.Infinity => MathF.Min(
            MathF.Abs(Vector2.Distance(p, new Vector2(-0.55f, 0.0f)) - 0.42f),
            MathF.Abs(Vector2.Distance(p, new Vector2(0.55f, 0.0f)) - 0.42f)) - 0.11f,
        IconShape.Diamond => (MathF.Abs(p.X) + MathF.Abs(p.Y)) / 1.4142f - 0.62f,
        _ => Polygon(p, BoltShape),
    };

    private static readonly Vector2[] BoltShape =
    {
        new(0.15f, -0.95f), new(-0.55f, 0.15f), new(-0.05f, 0.15f), new(-0.2f, 0.95f), new(0.55f, -0.2f), new(0.05f, -0.2f),
    };

    private static float Union(params float[] distances)
    {
        float d = float.MaxValue;
        foreach (float v in distances) d = MathF.Min(d, v);
        return d;
    }

    private static float Circle(Vector2 p, Vector2 center, float radius) => Vector2.Distance(p, center) - radius;

    private static float Segment(Vector2 p, Vector2 a, Vector2 b, float radius)
    {
        Vector2 pa = p - a;
        Vector2 ba = b - a;
        float t = Math.Clamp(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba), 0.0f, 1.0f);
        return (pa - ba * t).Length() - radius;
    }

    private static float Box(Vector2 p, Vector2 center, Vector2 half, float radius)
    {
        Vector2 q = Vector2.Abs(p - center) - half + new Vector2(radius);
        return Vector2.Max(q, Vector2.Zero).Length() + MathF.Min(MathF.Max(q.X, q.Y), 0.0f) - radius;
    }

    // Distance to the nearest edge, negative inside (even-odd crossing test).
    private static float Polygon(Vector2 p, Vector2[] vertices)
    {
        float nearest = float.MaxValue;
        bool inside = false;
        for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
        {
            Vector2 a = vertices[j];
            Vector2 b = vertices[i];
            nearest = MathF.Min(nearest, Segment(p, a, b, 0.0f));
            if ((b.Y > p.Y) != (a.Y > p.Y) && p.X < (a.X - b.X) * (p.Y - b.Y) / (a.Y - b.Y) + b.X)
            {
                inside = !inside;
            }
        }

        return inside ? -nearest : nearest;
    }

    // Antialiased coverage of a pixel whose center lies d pixels outside the shape's edge.
    private static float Coverage(float d) => Math.Clamp(0.5f - d, 0.0f, 1.0f);

    // Signed distance from a pixel center to a rounded rectangle filling the texture less a 1-pixel margin.
    private static float RoundedDistance(float x, float y, float w, float h, float radius)
    {
        var half = new Vector2(w * 0.5f - 1.0f, h * 0.5f - 1.0f);
        Vector2 p = new Vector2(MathF.Abs(x - w * 0.5f), MathF.Abs(y - h * 0.5f)) - half + new Vector2(radius);
        Vector2 outside = Vector2.Max(p, Vector2.Zero);
        return outside.Length() + MathF.Min(MathF.Max(p.X, p.Y), 0.0f) - radius;
    }

    private static Texture2D Generate(int width, int height, Func<float, float, float, float, float> alpha)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                float a = Math.Clamp(alpha(x + 0.5f, y + 0.5f, width, height), 0.0f, 1.0f);
                pixels[i] = 255;
                pixels[i + 1] = 255;
                pixels[i + 2] = 255;
                pixels[i + 3] = (byte)MathF.Round(a * 255.0f);
            }
        }

        return new Texture2D((uint)width, (uint)height, pixels);
    }
}
