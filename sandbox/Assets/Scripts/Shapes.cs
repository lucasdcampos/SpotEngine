using System;
using System.Numerics;
using Spot.Rendering;

namespace Spot.Game;

/// <summary>
/// Procedurally generated white shape textures (a solid interior with an alpha cut-out) so 2D sprites
/// can be circles, triangles and polygons instead of only squares. A sprite's <c>Color</c> tints the
/// white shape. Each texture is built once, on first use, on the render thread and cached for the rest
/// of the process. The engine's 2D sprite shader alpha-tests these, so the cut-out reads as the shape.
/// </summary>
public static class Shapes
{
    private const int Size = 64;

    private static Texture2D? s_circle;
    private static Texture2D? s_triangle;
    private static Texture2D? s_diamond;
    private static Texture2D? s_pentagon;
    private static Texture2D? s_hexagon;

    /// <summary>A filled circle.</summary>
    public static Texture2D Circle => s_circle ??= Generate((u, v) => u * u + v * v <= 0.92f * 0.92f);

    /// <summary>A filled triangle with one vertex pointing along +X (so it aims where the sprite faces).</summary>
    public static Texture2D Triangle => s_triangle ??= RegularPolygon(3);

    /// <summary>A filled diamond (a square rotated 45°), one vertex along +X.</summary>
    public static Texture2D Diamond => s_diamond ??= RegularPolygon(4);

    /// <summary>A filled regular pentagon.</summary>
    public static Texture2D Pentagon => s_pentagon ??= RegularPolygon(5);

    /// <summary>A filled regular hexagon.</summary>
    public static Texture2D Hexagon => s_hexagon ??= RegularPolygon(6);

    // Builds a texture for a regular polygon inscribed in the unit circle, with the first vertex on +X.
    private static Texture2D RegularPolygon(int sides)
    {
        var verts = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = i * (MathF.Tau / sides);
            verts[i] = new Vector2(MathF.Cos(a), MathF.Sin(a)) * 0.95f;
        }

        return Generate((u, v) => InsideConvex(verts, u, v));
    }

    // Rasterizes a shape into a white RGBA texture: interior pixels are opaque, exterior fully transparent.
    private static Texture2D Generate(Func<float, float, bool> inside)
    {
        byte[] pixels = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size * 2.0f - 1.0f;
                float v = (y + 0.5f) / Size * 2.0f - 1.0f;
                int idx = (y * Size + x) * 4;
                pixels[idx] = 255;
                pixels[idx + 1] = 255;
                pixels[idx + 2] = 255;
                pixels[idx + 3] = inside(u, v) ? (byte)255 : (byte)0;
            }
        }

        return new Texture2D(Size, Size, pixels);
    }

    // Convex point-in-polygon test: the point is inside when it is on the same side of every edge.
    private static bool InsideConvex(Vector2[] verts, float px, float py)
    {
        bool? positive = null;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector2 a = verts[i];
            Vector2 b = verts[(i + 1) % verts.Length];
            float cross = (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);
            bool sign = cross >= 0.0f;
            if (positive is null)
            {
                positive = sign;
            }
            else if (positive != sign)
            {
                return false;
            }
        }

        return true;
    }
}
