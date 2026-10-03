using System.Numerics;
using Spot.Framework.Graphics;

namespace SolarSystem;

/// <summary>The icons <see cref="UITextures.Icon"/> draws.</summary>
internal enum IconShape
{
    Pause,
    Play,
    Settings,
    Overview,
    Tour,
    Close,
}

/// <summary>
/// Small textures generated at startup — rounded panels, their outline and shadow, discs, rings — so the HUD gets
/// soft, antialiased shapes without shipping a single image. White, with the shape in alpha, so a widget's color
/// tints them.
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
        Generate(size, size, (x, y, w, h) =>
            Coverage(Vector2.Distance(new Vector2(x, y), new Vector2(w, h) * 0.5f) - w * 0.5f + 1.0f));

    /// <summary>A circle outline <paramref name="stroke"/> pixels wide.</summary>
    public static Texture2D Ring(int size = 128, float stroke = 2.0f) =>
        Generate(size, size, (x, y, w, h) =>
        {
            float d = MathF.Abs(Vector2.Distance(new Vector2(x, y), new Vector2(w, h) * 0.5f) - (w * 0.5f - stroke - 1.0f));
            return Coverage(d - stroke * 0.5f);
        });

    /// <summary>A soft line across the texture's height, for the orbit ribbons.</summary>
    public static Texture2D LineProfile() =>
        Generate(4, 32, (x, y, w, h) =>
        {
            float v = (y - h * 0.5f) / (h * 0.5f);
            return MathF.Exp(-v * v * 5.0f) * (1.0f - v * v);
        });

    /// <summary>
    /// A toolbar icon, drawn from signed distances in a -1..1 square (y down) so its edges stay smooth at any size.
    /// </summary>
    public static Texture2D Icon(IconShape shape, int size = 64) =>
        Generate(size, size, (x, y, w, h) =>
        {
            var p = new Vector2(x / w * 2.0f - 1.0f, y / h * 2.0f - 1.0f);
            return Coverage(IconDistance(shape, p) * w * 0.5f);
        });

    private static float IconDistance(IconShape shape, Vector2 p) => shape switch
    {
        IconShape.Pause => MathF.Min(Segment(p, new(-0.3f, -0.45f), new(-0.3f, 0.45f), 0.13f),
            Segment(p, new(0.3f, -0.45f), new(0.3f, 0.45f), 0.13f)),
        IconShape.Play => Polygon(p, PlayTriangle) - 0.08f,
        IconShape.Settings => Gear(p),
        IconShape.Overview => OrbitIcon(p),
        IconShape.Tour => MathF.Min(MathF.Abs(p.Length() - 0.78f) - 0.08f, Polygon(p, CompassNeedle) - 0.02f),
        _ => MathF.Min(Segment(p, new(-0.45f, -0.45f), new(0.45f, 0.45f), 0.09f),
            Segment(p, new(-0.45f, 0.45f), new(0.45f, -0.45f), 0.09f)),
    };

    private static readonly Vector2[] PlayTriangle = { new(-0.28f, -0.5f), new(0.52f, 0.0f), new(-0.28f, 0.5f) };
    private static readonly Vector2[] CompassNeedle = { new(0.4f, -0.4f), new(0.13f, 0.13f), new(-0.4f, 0.4f), new(-0.13f, -0.13f) };

    // A hub with eight teeth and a hole.
    private static float Gear(Vector2 p)
    {
        float gear = p.Length() - 0.56f;
        for (int k = 0; k < 8; k++)
        {
            float angle = k * MathF.PI / 4.0f;
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);
            var q = new Vector2(p.X * c + p.Y * s, -p.X * s + p.Y * c);
            gear = MathF.Min(gear, Box(q, new Vector2(0.68f, 0.0f), new Vector2(0.16f, 0.13f), 0.05f));
        }

        return MathF.Max(gear, 0.23f - p.Length());
    }

    // A sun, the circle of an orbit, and a planet on it with a gap cut around it.
    private static float OrbitIcon(Vector2 p)
    {
        const float radius = 0.72f;
        var planet = new Vector2(radius * MathF.Cos(-0.8f), radius * MathF.Sin(-0.8f));
        float orbit = MathF.Max(MathF.Abs(p.Length() - radius) - 0.065f, 0.27f - Vector2.Distance(p, planet));
        float sun = p.Length() - 0.24f;
        return MathF.Min(MathF.Min(sun, orbit), Vector2.Distance(p, planet) - 0.15f);
    }

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
