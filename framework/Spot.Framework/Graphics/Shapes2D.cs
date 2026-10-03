using System.Numerics;

namespace Spot.Framework.Graphics;

/// <summary>
/// Shapes and sprites for <see cref="Renderer2D"/>, built on its quad-from-corners primitive and batched with
/// everything else drawn in the same scene: <c>Renderer2D.DrawCircle</c>, <c>DrawTriangle</c>, <c>DrawPolygon</c>
/// and <c>DrawSprite</c>.
/// </summary>
public static class Shapes2D
{
    /// <summary>The fewest segments a circle is drawn with.</summary>
    public const int MinCircleSegments = 3;

    extension(Renderer2D)
    {
        /// <summary>
        /// Draws a filled triangle.
        /// </summary>
        /// <param name="a">The first corner.</param>
        /// <param name="b">The second corner.</param>
        /// <param name="c">The third corner.</param>
        /// <param name="color">The RGBA color.</param>
        public static void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color)
        {
            var pc = new Vector3(c, 0.0f);
            Renderer2D.DrawQuad(new Vector3(a, 0.0f), new Vector3(b, 0.0f), pc, pc, color);
        }

        /// <summary>
        /// Draws a filled circle as a fan of <paramref name="segments"/> triangles.
        /// </summary>
        /// <param name="center">The center.</param>
        /// <param name="radius">The radius. Zero or negative draws nothing.</param>
        /// <param name="color">The RGBA color.</param>
        /// <param name="segments">How many edges approximate the circle (at least <see cref="MinCircleSegments"/>).</param>
        public static void DrawCircle(Vector2 center, float radius, Vector4 color, int segments = 32)
        {
            if (radius <= 0.0f)
            {
                return;
            }

            segments = Math.Max(MinCircleSegments, segments);
            var c = new Vector3(center, 0.0f);
            Vector3 previous = c + new Vector3(radius, 0.0f, 0.0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = MathF.Tau * i / segments;
                Vector3 next = c + new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0.0f);
                Renderer2D.DrawQuad(c, previous, next, next, color);
                previous = next;
            }
        }

        /// <summary>
        /// Draws a filled convex polygon as a fan from its first point. Fewer than three points draws nothing.
        /// </summary>
        /// <param name="points">The polygon's corners, in order (either winding).</param>
        /// <param name="color">The RGBA color.</param>
        public static void DrawPolygon(ReadOnlySpan<Vector2> points, Vector4 color)
        {
            for (int i = 1; i + 1 < points.Length; i++)
            {
                Renderer2D.DrawTriangle(points[0], points[i], points[i + 1], color);
            }
        }

        /// <summary>
        /// Draws a sprite: a textured quad centered on <paramref name="position"/>, optionally cut from a region of
        /// an atlas, rotated, tinted and flipped.
        /// </summary>
        /// <param name="texture">The texture (or atlas).</param>
        /// <param name="position">The sprite's center.</param>
        /// <param name="size">The sprite's width and height.</param>
        /// <param name="sourceRect">
        /// The region to draw as <c>(x, y, width, height)</c> in texture pixels with a top-left origin — as an image
        /// editor or atlas tool reports it. <see langword="null"/> draws the whole texture.
        /// </param>
        /// <param name="rotation">The rotation around the center, in radians (counter-clockwise).</param>
        /// <param name="tint">The color multiplied with the texture; <see langword="null"/> for white.</param>
        /// <param name="flipX">Mirror the sprite horizontally.</param>
        /// <param name="flipY">Mirror the sprite vertically.</param>
        public static void DrawSprite(Texture2D texture, Vector2 position, Vector2 size, Vector4? sourceRect = null,
            float rotation = 0.0f, Vector4? tint = null, bool flipX = false, bool flipY = false)
        {
            ArgumentNullException.ThrowIfNull(texture);

            Vector4 uv = SpriteUv(texture.Width, texture.Height, sourceRect);
            if (flipX)
            {
                (uv.X, uv.Z) = (uv.Z, uv.X);
            }

            if (flipY)
            {
                (uv.Y, uv.W) = (uv.W, uv.Y);
            }

            Vector2 half = size * 0.5f;
            float cos = MathF.Cos(rotation);
            float sin = MathF.Sin(rotation);
            Vector3 Corner(float x, float y) =>
                new(position.X + x * cos - y * sin, position.Y + x * sin + y * cos, 0.0f);

            Renderer2D.DrawQuad(
                Corner(-half.X, -half.Y), Corner(half.X, -half.Y), Corner(half.X, half.Y), Corner(-half.X, half.Y),
                tint ?? Vector4.One, texture, uv);
        }
    }

    /// <summary>
    /// Converts a pixel region with a top-left origin into the bottom-up UV rectangle a GPU texture uses
    /// (textures are stored bottom-to-top). <see langword="null"/> is the whole texture.
    /// </summary>
    /// <param name="textureWidth">The texture width in pixels.</param>
    /// <param name="textureHeight">The texture height in pixels.</param>
    /// <param name="sourceRect">The region as <c>(x, y, width, height)</c> in pixels, top-left origin.</param>
    /// <returns>The UV rectangle as <c>(u0, v0, u1, v1)</c>, bottom-left to top-right.</returns>
    public static Vector4 SpriteUv(uint textureWidth, uint textureHeight, Vector4? sourceRect)
    {
        if (sourceRect is not { } r || textureWidth == 0 || textureHeight == 0)
        {
            return new Vector4(0.0f, 0.0f, 1.0f, 1.0f);
        }

        float w = textureWidth;
        float h = textureHeight;
        return new Vector4(r.X / w, 1.0f - (r.Y + r.W) / h, (r.X + r.Z) / w, 1.0f - r.Y / h);
    }
}
