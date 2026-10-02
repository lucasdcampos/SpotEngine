using System;
using System.Numerics;
using ImGuiNET;

namespace Spot.Editor.UI;

/// <summary>
/// Painted Asset Browser icons: small illustrations built from draw-list shapes with gradients, highlights and soft
/// shadows instead of icon-font glyphs. Each painter fills the square box at <c>min</c> with side <c>size</c> and
/// stays legible from 48 to 128 px. Files share one visual language (a paper page with a folded corner and a type
/// badge); folders, scenes, images, models, prefabs and materials get their own renderings. Asset-type colors are
/// kept to accents. Depends on ImGui only, so the painters can be rendered outside the editor.
/// </summary>
internal static class AssetIcons
{
    // Asset-type accents, for badges and highlights here and for the tile's type divider.
    public static readonly Vector4 ScriptAccent = Rgb(146, 104, 214);
    public static readonly Vector4 SceneAccent = Rgb(92, 146, 230);
    public static readonly Vector4 ImageAccent = Rgb(86, 178, 132);
    public static readonly Vector4 ModelAccent = Rgb(226, 146, 82);
    public static readonly Vector4 MaterialAccent = Rgb(214, 108, 104);
    public static readonly Vector4 PrefabAccent = Rgb(78, 160, 222);
    public static readonly Vector4 AudioAccent = Rgb(214, 102, 152);
    public static readonly Vector4 ControllerAccent = Rgb(218, 168, 70);
    public static readonly Vector4 UIAccent = Rgb(64, 178, 162);
    public static readonly Vector4 FolderAccent = Rgb(196, 162, 106);
    public static readonly Vector4 FileAccent = Rgb(128, 134, 146);

    // Paper for documents and the photo mat: a cool light gray, not white, so it doesn't glare on the dark panel.
    private static readonly Vector4 PaperTop = Rgb(214, 218, 225);
    private static readonly Vector4 PaperBottom = Rgb(166, 172, 183);
    private static readonly Vector4 Ink = Rgb(112, 119, 132);

    // Neutral clay, matching the material the live model thumbnails are rendered with.
    private static readonly Vector4 Clay = Rgb(176, 178, 186);

    private const uint White = 0xFFFFFFFF;

    // ---------------------------------------------------------------------------------------------------------
    // Folders
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>A folder with a tabbed back panel; papers peek out when it holds anything.</summary>
    public static void Folder(ImDrawListPtr dl, Vector2 min, float size, bool hasContents)
    {
        float x0 = min.X + size * 0.12f;
        float x1 = min.X + size * 0.88f;
        float backTop = min.Y + size * 0.27f;
        float frontTop = backTop + size * 0.11f;
        float bottom = min.Y + size * 0.80f;
        float w = x1 - x0;
        float r = size * 0.05f;

        SoftShadow(dl, new Vector2(x0, backTop + size * 0.05f), new Vector2(x1, bottom + size * 0.035f), r, size * 0.10f, 0.65f);

        // Back panel and its tab share one gradient, so the seam between the two shapes doesn't show.
        Vector2 g0 = new(x0, backTop - size * 0.08f);
        Vector2 g1 = new(x0, bottom);
        Vector4 backTopColor = Rgb(138, 112, 74);
        Vector4 backBottomColor = Rgb(96, 76, 48);
        float tabW = w * 0.36f;
        float tabH = size * 0.075f;
        dl.PathClear();
        dl.PathArcToFast(new Vector2(x0 + r, backTop - tabH + r), r, 6, 9);
        dl.PathLineTo(new Vector2(x0 + tabW, backTop - tabH));
        dl.PathLineTo(new Vector2(x0 + tabW + tabH * 1.1f, backTop + r));
        dl.PathLineTo(new Vector2(x0, backTop + r));
        FillPath(dl, g0, g1, backTopColor, backBottomColor);
        Rect(dl, new Vector2(x0, backTop), new Vector2(x1, bottom), r, backTopColor, backBottomColor);
        HLine(dl, x0 + r, x0 + tabW, backTop - tabH + 0.5f, new Vector4(1, 1, 1, 0.18f));
        HLine(dl, x0 + tabW + tabH * 1.1f, x1 - r, backTop + 0.5f, new Vector4(1, 1, 1, 0.18f));

        if (hasContents)
        {
            Rect(dl, new Vector2(x0 + w * 0.10f, backTop - size * 0.035f), new Vector2(x1 - w * 0.16f, frontTop + r),
                size * 0.02f, Rgb(190, 194, 202), Rgb(160, 165, 175));
            Rect(dl, new Vector2(x0 + w * 0.17f, backTop + size * 0.005f), new Vector2(x1 - w * 0.08f, frontTop + r),
                size * 0.02f, Rgb(236, 238, 242), Rgb(204, 208, 215));
        }

        // Front panel: lit along its top edge, falling off toward the bottom.
        Rect(dl, new Vector2(x0, frontTop), new Vector2(x1, bottom), r, Rgb(214, 180, 122), Rgb(166, 132, 80));
        HLine(dl, x0 + r, x1 - r, frontTop + 0.5f, new Vector4(1, 1, 1, 0.45f));
        HLine(dl, x0 + r, x1 - r, bottom - 0.5f, new Vector4(0, 0, 0, 0.18f));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Documents
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>A C# source file: syntax-tinted code lines and a "C#" badge.</summary>
    public static void Script(ImDrawListPtr dl, Vector2 min, float size, ImFontPtr font)
    {
        (Vector2 a, Vector2 b) = Page(dl, min, size);
        TextLines(dl, a, b, size, ScriptAccent);
        (Vector2 center, Vector2 half) = BadgeBox(a, b, 0.14f);
        Badge(dl, center, half, ScriptAccent, size);
        Label(dl, font, center, "C#", half.Y * 1.85f, half.X * 1.55f, Vector4.One);
    }

    /// <summary>A UI document: a small layout wireframe and a "UI" badge.</summary>
    public static void UIDocument(ImDrawListPtr dl, Vector2 min, float size, ImFontPtr font)
    {
        (Vector2 a, Vector2 b) = Page(dl, min, size);
        float w = b.X - a.X;
        float h = b.Y - a.Y;
        float rr = MathF.Max(1.0f, size * 0.012f);
        Vector4 block = WithAlpha(Ink, 0.85f);
        Rect(dl, new Vector2(a.X + w * 0.14f, a.Y + h * 0.17f), new Vector2(a.X + w * 0.62f, a.Y + h * 0.235f), rr,
            Lighten(block, 0.1f), block);
        Rect(dl, new Vector2(a.X + w * 0.14f, a.Y + h * 0.29f), new Vector2(a.X + w * 0.47f, a.Y + h * 0.45f), rr,
            WithAlpha(UIAccent, 0.75f), WithAlpha(Darken(UIAccent, 0.8f), 0.75f));
        Rect(dl, new Vector2(a.X + w * 0.53f, a.Y + h * 0.29f), new Vector2(a.X + w * 0.86f, a.Y + h * 0.45f), rr,
            Lighten(block, 0.1f), block);

        (Vector2 center, Vector2 half) = BadgeBox(a, b, 0.14f);
        Badge(dl, center, half, UIAccent, size);
        Label(dl, font, center, "UI", half.Y * 1.85f, half.X * 1.55f, Vector4.One);
    }

    /// <summary>An audio file: a waveform badge.</summary>
    public static void Audio(ImDrawListPtr dl, Vector2 min, float size)
    {
        (Vector2 a, Vector2 b) = Page(dl, min, size);
        TextLines(dl, a, b, size, null);
        (Vector2 center, Vector2 half) = BadgeBox(a, b, 0.15f);
        Badge(dl, center, half, AudioAccent, size);

        ReadOnlySpan<float> heights = [0.30f, 0.55f, 0.90f, 0.50f, 1.00f, 0.70f, 0.40f, 0.80f, 0.35f];
        float barW = MathF.Max(1.0f, half.X * 0.085f);
        float step = half.X * 1.60f / (heights.Length - 1);
        float x = center.X - half.X * 0.80f;
        foreach (float k in heights)
        {
            float bh = MathF.Max(barW, half.Y * 1.30f * k);
            dl.AddRectFilled(new Vector2(x - barW * 0.5f, center.Y - bh * 0.5f), new Vector2(x + barW * 0.5f, center.Y + bh * 0.5f),
                U32(new Vector4(1, 1, 1, 0.95f)), barW * 0.5f);
            x += step;
        }
    }

    /// <summary>An animator controller: a badge with a small state graph.</summary>
    public static void AnimatorController(ImDrawListPtr dl, Vector2 min, float size)
    {
        (Vector2 a, Vector2 b) = Page(dl, min, size);
        TextLines(dl, a, b, size, null);
        (Vector2 center, Vector2 half) = BadgeBox(a, b, 0.17f);
        Badge(dl, center, half, ControllerAccent, size);

        Vector2 node = new(half.X * 0.42f, half.Y * 0.62f);
        Vector2 left = center + new Vector2(-half.X * 0.46f, 0.0f);
        Vector2 rightTop = center + new Vector2(half.X * 0.48f, -half.Y * 0.42f);
        Vector2 rightBottom = center + new Vector2(half.X * 0.48f, half.Y * 0.42f);
        float th = MathF.Max(1.0f, size * 0.014f);
        uint wire = U32(new Vector4(1, 1, 1, 0.80f));
        dl.AddLine(left + new Vector2(node.X * 0.5f, 0), rightTop - new Vector2(node.X * 0.5f, 0), wire, th);
        dl.AddLine(left + new Vector2(node.X * 0.5f, 0), rightBottom - new Vector2(node.X * 0.5f, 0), wire, th);
        uint fill = U32(new Vector4(1, 1, 1, 0.96f));
        ReadOnlySpan<Vector2> nodes = [left, rightTop, rightBottom];
        foreach (Vector2 c in nodes)
        {
            dl.AddRectFilled(c - node * 0.5f, c + node * 0.5f, fill, node.Y * 0.3f);
        }
    }

    /// <summary>Any other file: text lines, plus a neutral badge with <paramref name="label"/> (e.g. the extension) when given.</summary>
    public static void File(ImDrawListPtr dl, Vector2 min, float size, ImFontPtr font, string? label)
    {
        (Vector2 a, Vector2 b) = Page(dl, min, size);
        if (string.IsNullOrEmpty(label))
        {
            TextLines(dl, a, b, size, null, lines: 5);
            return;
        }

        TextLines(dl, a, b, size, null);
        (Vector2 center, Vector2 half) = BadgeBox(a, b, 0.13f);
        Badge(dl, center, half, FileAccent, size);
        Label(dl, font, center, label, half.Y * 1.7f, half.X * 1.6f, Vector4.One);
    }

    // The shared document: a paper page with a folded top-right corner on a soft shadow. Returns the page's box.
    private static (Vector2 Min, Vector2 Max) Page(ImDrawListPtr dl, Vector2 min, float size)
    {
        float w = size * 0.60f;
        float h = size * 0.76f;
        Vector2 a = min + new Vector2((size - w) * 0.5f, size * 0.09f);
        Vector2 b = a + new Vector2(w, h);
        float r = size * 0.045f;
        float f = w * 0.30f;

        // A soft shadow in the page's own shape (so none shows behind the cut corner): stacked outlines grown from
        // inset to outset. Growing the outline by g pushes the 45° cut out by g, lengthening it by g * (2 - √2).
        Vector2 drop = new(0.0f, size * 0.03f);
        float blur = size * 0.09f;
        const int Layers = 6;
        uint shadow = U32(new Vector4(0, 0, 0, 0.16f));
        for (int i = 0; i < Layers; i++)
        {
            float grow = blur * ((i + 1) / (float)Layers - 0.5f);
            PagePath(dl, a + drop - new Vector2(grow), b + drop + new Vector2(grow), MathF.Max(0.0f, r + grow), f + grow * 0.586f);
            dl.PathFillConvex(shadow);
        }

        PagePath(dl, a, b, r, f);
        FillPath(dl, a, new Vector2(a.X, b.Y), PaperTop, PaperBottom);
        HLine(dl, a.X + r, b.X - f, a.Y + 0.5f, new Vector4(1, 1, 1, 0.6f));

        // The flap (the paper's back) casts a faint shadow on the page and is lit along its crease.
        Vector2 top = new(b.X - f, a.Y);
        Vector2 corner = new(b.X - f, a.Y + f);
        Vector2 right = new(b.X, a.Y + f);
        Vector2 offset = new(-f * 0.07f, f * 0.11f);
        Poly(dl, [top + offset, right + offset, corner + offset], top, corner,
            new Vector4(0, 0, 0, 0.22f), new Vector4(0, 0, 0, 0.22f));
        Poly(dl, [top, right, corner], corner, (top + right) * 0.5f, Rgb(178, 184, 195), Rgb(236, 239, 244));
        return (a, b);
    }

    // Traces a page outline: rounded corners, except the top-right one, cut at 45° by f for the fold.
    private static void PagePath(ImDrawListPtr dl, Vector2 a, Vector2 b, float r, float f)
    {
        dl.PathClear();
        dl.PathArcToFast(new Vector2(a.X + r, a.Y + r), r, 6, 9);
        dl.PathLineTo(new Vector2(b.X - f, a.Y));
        dl.PathLineTo(new Vector2(b.X, a.Y + f));
        dl.PathArcToFast(new Vector2(b.X - r, b.Y - r), r, 0, 3);
        dl.PathArcToFast(new Vector2(a.X + r, b.Y - r), r, 3, 6);
    }

    // Short horizontal strokes standing in for text; with a keyword color, each line starts with a tinted token.
    private static void TextLines(ImDrawListPtr dl, Vector2 a, Vector2 b, float size, Vector4? keyword, int lines = 3)
    {
        ReadOnlySpan<float> lengths = [0.44f, 0.66f, 0.38f, 0.60f, 0.50f];
        float w = b.X - a.X;
        float h = b.Y - a.Y;
        float th = MathF.Max(1.0f, size * 0.026f);
        float x = a.X + w * 0.14f;
        for (int i = 0; i < lines && i < lengths.Length; i++)
        {
            float y = a.Y + h * (0.22f + i * 0.09f);
            float end = x + w * lengths[i];
            float start = x;
            if (keyword is Vector4 k)
            {
                float split = x + w * (i == 1 ? 0.24f : 0.16f);
                dl.AddRectFilled(new Vector2(start, y), new Vector2(split, y + th), U32(Darken(k, 0.9f)), th * 0.5f);
                start = split + w * 0.05f;
            }
            dl.AddRectFilled(new Vector2(start, y), new Vector2(end, y + th), U32(WithAlpha(Ink, 0.9f)), th * 0.5f);
        }
    }

    // The badge's center and half-extents on a page: centered, in the lower part of the sheet.
    private static (Vector2 Center, Vector2 Half) BadgeBox(Vector2 a, Vector2 b, float halfHeight)
    {
        float w = b.X - a.X;
        float h = b.Y - a.Y;
        return (new Vector2((a.X + b.X) * 0.5f, a.Y + h * 0.69f), new Vector2(w * 0.40f, h * halfHeight));
    }

    // A rounded, lit, accent-colored plate with a soft shadow.
    private static void Badge(ImDrawListPtr dl, Vector2 center, Vector2 half, Vector4 accent, float size)
    {
        Vector2 a = center - half;
        Vector2 b = center + half;
        float r = half.Y * 0.45f;
        Vector2 drop = new(0.0f, size * 0.012f);
        SoftShadow(dl, a + drop, b + drop, r, size * 0.035f, 0.45f);
        Rect(dl, a, b, r, Lighten(accent, 0.12f), Darken(accent, 0.72f));
        HLine(dl, a.X + r, b.X - r, a.Y + 0.5f, new Vector4(1, 1, 1, 0.35f));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Scenes and images
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>A scene: a little viewport onto a ground grid under an evening sky, with a cube and a sphere.</summary>
    public static void Scene(ImDrawListPtr dl, Vector2 min, float size)
    {
        Vector2 a = min + new Vector2(size * 0.10f, size * 0.17f);
        Vector2 b = min + new Vector2(size * 0.90f, size * 0.83f);
        float w = b.X - a.X;
        float h = b.Y - a.Y;
        float r = size * 0.06f;
        Vector2 drop = new(0.0f, size * 0.03f);
        SoftShadow(dl, a + drop, b + drop, r, size * 0.09f, 0.65f);

        float horizon = a.Y + h * 0.47f;
        Rect(dl, a, new Vector2(b.X, horizon), r, Rgb(30, 44, 74), Rgb(104, 132, 172), ImDrawFlags.RoundCornersTop);
        Rect(dl, new Vector2(a.X, horizon), b, r, Rgb(58, 64, 76), Rgb(28, 31, 38), ImDrawFlags.RoundCornersBottom);

        // A low sun with a soft glow.
        Vector2 sun = new(a.X + w * 0.77f, a.Y + h * 0.22f);
        float sunR = size * 0.045f;
        for (int i = 4; i >= 1; i--)
        {
            dl.AddCircleFilled(sun, sunR * (1.0f + i * 0.45f), U32(new Vector4(1.0f, 0.86f, 0.66f, 0.07f)), 32);
        }
        dl.AddCircleFilled(sun, sunR, U32(Rgb(255, 236, 206)), 32);

        // Perspective grid converging on the horizon, fading into the distance.
        float th = MathF.Max(1.0f, size * 0.011f);
        dl.PushClipRect(new Vector2(a.X, horizon), b, true);
        int start = dl.VtxBuffer.Size;
        Vector2 vanish = new((a.X + b.X) * 0.5f, horizon);
        for (int k = -6; k <= 6; k++)
        {
            dl.AddLine(vanish, new Vector2(vanish.X + k * w * 0.19f, b.Y + h * 0.1f), White, th);
        }
        ReadOnlySpan<float> depths = [1.3f, 1.9f, 3.0f, 5.0f];
        foreach (float depth in depths)
        {
            float y = horizon + (b.Y - horizon) / depth;
            dl.AddLine(new Vector2(a.X, y), new Vector2(b.X, y), White, th);
        }
        Vector4 grid = Lighten(SceneAccent, 0.15f);
        ApplyGradient(dl, start, vanish, new Vector2(vanish.X, b.Y), WithAlpha(grid, 0.0f), WithAlpha(grid, 0.55f));
        dl.PopClipRect();
        HLine(dl, a.X, b.X, horizon, new Vector4(1.0f, 0.9f, 0.8f, 0.25f));

        // A cube and a sphere standing on the grid, each with a contact shadow.
        float e = size * 0.105f;
        Vector2 cube = new(a.X + w * 0.36f, horizon + (b.Y - horizon) * 0.62f - e);
        EllipseShadow(dl, cube + new Vector2(0.0f, e * 0.85f), new Vector2(e * 1.15f, e * 0.38f), 0.6f);
        IsoCube(dl, cube, e, Clay);

        float sr = size * 0.075f;
        Vector2 ball = new(a.X + w * 0.66f, horizon + (b.Y - horizon) * 0.64f - sr);
        EllipseShadow(dl, ball + new Vector2(0.0f, sr * 0.92f), new Vector2(sr * 1.0f, sr * 0.30f), 0.6f);
        Sphere(dl, ball, sr, Rgb(222, 146, 92));

        dl.AddRect(a, b, U32(new Vector4(1, 1, 1, 0.10f)), r, ImDrawFlags.None, 1.0f);
    }

    /// <summary>A picture: a matted photo of hills under a sun.</summary>
    public static void Image(ImDrawListPtr dl, Vector2 min, float size)
    {
        Vector2 a = min + new Vector2(size * 0.12f, size * 0.18f);
        Vector2 b = min + new Vector2(size * 0.88f, size * 0.82f);
        float r = size * 0.045f;
        Vector2 drop = new(0.0f, size * 0.03f);
        SoftShadow(dl, a + drop, b + drop, r, size * 0.09f, 0.6f);
        Rect(dl, a, b, r, PaperTop, PaperBottom);
        HLine(dl, a.X + r, b.X - r, a.Y + 0.5f, new Vector4(1, 1, 1, 0.6f));

        float m = size * 0.055f;
        Vector2 ia = a + new Vector2(m, m);
        Vector2 ib = b - new Vector2(m, m);
        float iw = ib.X - ia.X;
        float ih = ib.Y - ia.Y;
        Rect(dl, ia, ib, 0.0f, Rgb(62, 104, 150), Rgb(150, 184, 204));

        dl.PushClipRect(ia, ib, true);
        Vector2 sun = new(ia.X + iw * 0.74f, ia.Y + ih * 0.30f);
        dl.AddCircleFilled(sun, size * 0.085f, U32(new Vector4(1.0f, 0.9f, 0.7f, 0.18f)), 32);
        dl.AddCircleFilled(sun, size * 0.05f, U32(Rgb(255, 228, 170)), 32);
        Poly(dl, [new(ia.X + iw * 0.20f, ib.Y), new(ia.X + iw * 0.58f, ia.Y + ih * 0.32f), new(ia.X + iw * 1.05f, ib.Y)],
            new Vector2(0, ia.Y + ih * 0.32f), new Vector2(0, ib.Y), Rgb(96, 122, 150), Rgb(58, 76, 100));
        Poly(dl, [new(ia.X - iw * 0.15f, ib.Y), new(ia.X + iw * 0.28f, ia.Y + ih * 0.50f), new(ia.X + iw * 0.78f, ib.Y)],
            new Vector2(0, ia.Y + ih * 0.50f), new Vector2(0, ib.Y), Lighten(ImageAccent, 0.05f), Darken(ImageAccent, 0.55f));
        dl.PopClipRect();
        dl.AddRect(ia, ib, U32(new Vector4(0, 0, 0, 0.28f)), 0.0f, ImDrawFlags.None, 1.0f);
    }

    // ---------------------------------------------------------------------------------------------------------
    // 3D assets
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>A model: a clay cube on a contact shadow, like the live model thumbnails.</summary>
    public static void Model(ImDrawListPtr dl, Vector2 min, float size)
    {
        float e = size * 0.29f;
        Vector2 c = min + new Vector2(size * 0.5f, size * 0.47f);
        EllipseShadow(dl, c + new Vector2(0.0f, e * 0.80f), new Vector2(e * 1.10f, e * 0.36f), 0.7f);
        IsoCube(dl, c, e, Clay);
    }

    /// <summary>A prefab: the cube in the prefab blue.</summary>
    public static void Prefab(ImDrawListPtr dl, Vector2 min, float size)
    {
        float e = size * 0.29f;
        Vector2 c = min + new Vector2(size * 0.5f, size * 0.47f);
        EllipseShadow(dl, c + new Vector2(0.0f, e * 0.80f), new Vector2(e * 1.10f, e * 0.36f), 0.7f);
        IsoCube(dl, c, e, PrefabAccent);
    }

    /// <summary>A material: a lit sphere with a specular highlight and a colored rim.</summary>
    public static void Material(ImDrawListPtr dl, Vector2 min, float size)
    {
        float radius = size * 0.31f;
        Vector2 c = min + new Vector2(size * 0.5f, size * 0.46f);
        EllipseShadow(dl, c + new Vector2(0.0f, radius * 0.96f), new Vector2(radius * 0.85f, radius * 0.22f), 0.65f);
        Sphere(dl, c, radius, Vector4.Lerp(Clay, MaterialAccent, 0.35f));
    }

    // An isometric cube centered on c (the hexagon's center) with edge e: lit top, mid left face, shaded right face.
    private static void IsoCube(ImDrawListPtr dl, Vector2 c, float e, Vector4 color)
    {
        float k = e * 0.8660254f;
        Vector2 top = c + new Vector2(0, -e);
        Vector2 upperRight = c + new Vector2(k, -e * 0.5f);
        Vector2 upperLeft = c + new Vector2(-k, -e * 0.5f);
        Vector2 lowerRight = c + new Vector2(k, e * 0.5f);
        Vector2 lowerLeft = c + new Vector2(-k, e * 0.5f);
        Vector2 bottom = c + new Vector2(0, e);

        Poly(dl, [top, upperRight, c, upperLeft], upperLeft, upperRight, Lighten(color, 0.30f), Lighten(color, 0.08f));
        Poly(dl, [upperLeft, c, bottom, lowerLeft], upperLeft, lowerLeft, Darken(color, 0.80f), Darken(color, 0.60f));
        Poly(dl, [c, upperRight, lowerRight, bottom], c, bottom, Darken(color, 0.56f), Darken(color, 0.40f));

        float th = MathF.Max(1.0f, e * 0.04f);
        uint lit = U32(new Vector4(1, 1, 1, 0.50f));
        dl.AddLine(upperLeft, c, lit, th);
        dl.AddLine(c, upperRight, lit, th);
        dl.AddLine(c, bottom, U32(new Vector4(1, 1, 1, 0.16f)), th);
        dl.AddLine(upperLeft, top, U32(new Vector4(1, 1, 1, 0.22f)), th);
        dl.AddLine(top, upperRight, U32(new Vector4(1, 1, 1, 0.22f)), th);
    }

    // A sphere lit from the upper left: stacked circles drifting toward the highlight, and a specular spot.
    private static void Sphere(ImDrawListPtr dl, Vector2 c, float radius, Vector4 color)
    {
        Vector4 shadow = Darken(color, 0.28f);
        Vector4 lit = Lighten(color, 0.30f);
        Vector2 highlight = c + new Vector2(-0.34f, -0.38f) * radius;
        const int Layers = 24;
        int segments = Math.Clamp((int)(radius * 1.2f), 24, 64);
        for (int i = 0; i < Layers; i++)
        {
            float t = i / (float)(Layers - 1);
            float eased = 1.0f - (1.0f - t) * (1.0f - t);
            dl.AddCircleFilled(Vector2.Lerp(c, highlight, t), radius * (1.0f - 0.88f * t), U32(Vector4.Lerp(shadow, lit, eased)), segments);
        }

        dl.AddCircleFilled(highlight, radius * 0.16f, U32(new Vector4(1, 1, 1, 0.18f)), segments);
        dl.AddCircleFilled(highlight, radius * 0.08f, U32(new Vector4(1, 1, 1, 0.55f)), segments);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Drawing helpers
    // ---------------------------------------------------------------------------------------------------------

    // Draws a short word centered on center, shrunk to fit maxWidth, with a faint drop shadow. Inter's heaviest
    // bundled weight is Medium, so the word is overdrawn at small offsets to give it more weight.
    private static void Label(ImDrawListPtr dl, ImFontPtr font, Vector2 center, string text, float px, float maxWidth, Vector4 color)
    {
        Vector2 ts = font.CalcTextSizeA(px, float.MaxValue, 0.0f, text);
        if (ts.X > maxWidth && ts.X > 0.0f)
        {
            float k = maxWidth / ts.X;
            px *= k;
            ts *= k;
        }

        float bold = px * 0.035f;
        Vector2 pos = center - ts * 0.5f - new Vector2(bold * 0.5f);
        dl.AddText(font, px, pos + new Vector2(0.0f, MathF.Max(1.0f, px * 0.05f)), U32(new Vector4(0, 0, 0, 0.30f)), text);
        uint col = U32(color);
        dl.AddText(font, px, pos, col, text);
        dl.AddText(font, px, pos + new Vector2(bold, 0.0f), col, text);
        dl.AddText(font, px, pos + new Vector2(0.0f, bold), col, text);
        dl.AddText(font, px, pos + new Vector2(bold, bold), col, text);
    }

    // A soft drop shadow: stacked rounded rects, from inset to outset at low alpha, approximate a blur of width blur.
    private static void SoftShadow(ImDrawListPtr dl, Vector2 a, Vector2 b, float rounding, float blur, float alpha)
    {
        const int Layers = 6;
        uint col = U32(new Vector4(0, 0, 0, alpha / Layers * 1.6f));
        for (int i = 0; i < Layers; i++)
        {
            float grow = blur * ((i + 1) / (float)Layers - 0.5f);
            Vector2 lo = a - new Vector2(grow);
            Vector2 hi = b + new Vector2(grow);
            if (hi.X <= lo.X || hi.Y <= lo.Y)
            {
                continue;
            }
            dl.AddRectFilled(lo, hi, col, MathF.Max(0.0f, rounding + grow));
        }
    }

    // A soft elliptical contact shadow: shrinking stacked ellipses darken toward the middle.
    private static void EllipseShadow(ImDrawListPtr dl, Vector2 c, Vector2 radius, float alpha)
    {
        const int Layers = 5;
        const int Segments = 40;
        Span<Vector2> points = stackalloc Vector2[Segments];
        uint col = U32(new Vector4(0, 0, 0, alpha / Layers * 1.5f));
        for (int layer = 0; layer < Layers; layer++)
        {
            float s = 1.0f - layer * 0.17f;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * MathF.Tau / Segments;
                points[i] = c + new Vector2(MathF.Cos(angle) * radius.X * s, MathF.Sin(angle) * radius.Y * s);
            }
            dl.AddConvexPolyFilled(ref points[0], Segments, col);
        }
    }

    private static void HLine(ImDrawListPtr dl, float x0, float x1, float y, Vector4 color)
    {
        dl.AddLine(new Vector2(x0, y), new Vector2(x1, y), U32(color), 1.0f);
    }

    // Fills a rounded rect with a vertical gradient.
    private static void Rect(ImDrawListPtr dl, Vector2 a, Vector2 b, float rounding, Vector4 top, Vector4 bottom,
        ImDrawFlags flags = ImDrawFlags.None)
    {
        int start = dl.VtxBuffer.Size;
        dl.AddRectFilled(a, b, White, rounding, flags);
        ApplyGradient(dl, start, a, new Vector2(a.X, b.Y), top, bottom);
    }

    // Fills a convex polygon (either winding) with a linear gradient from g0 to g1.
    private static void Poly(ImDrawListPtr dl, ReadOnlySpan<Vector2> points, Vector2 g0, Vector2 g1, Vector4 c0, Vector4 c1)
    {
        // ImGui's anti-aliased fill expects clockwise points on screen (y down), i.e. a positive signed area.
        float area = 0.0f;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 p = points[i];
            Vector2 q = points[(i + 1) % points.Length];
            area += p.X * q.Y - q.X * p.Y;
        }

        dl.PathClear();
        for (int i = 0; i < points.Length; i++)
        {
            dl.PathLineTo(points[area >= 0.0f ? i : points.Length - 1 - i]);
        }
        FillPath(dl, g0, g1, c0, c1);
    }

    // Fills the current path (clockwise, convex) with a linear gradient from g0 to g1.
    private static void FillPath(ImDrawListPtr dl, Vector2 g0, Vector2 g1, Vector4 c0, Vector4 c1)
    {
        int start = dl.VtxBuffer.Size;
        dl.PathFillConvex(White);
        ApplyGradient(dl, start, g0, g1, c0, c1);
    }

    // Recolors the vertices emitted since vtxStart along a linear gradient from (p0, c0) to (p1, c1). Shapes are
    // drawn opaque white first, so each vertex's alpha is its anti-aliasing coverage, which is kept.
    private static void ApplyGradient(ImDrawListPtr dl, int vtxStart, Vector2 p0, Vector2 p1, Vector4 c0, Vector4 c1)
    {
        Vector2 d = p1 - p0;
        float invLengthSq = 1.0f / MathF.Max(d.LengthSquared(), 1e-6f);
        int end = dl.VtxBuffer.Size;
        for (int i = vtxStart; i < end; i++)
        {
            ImDrawVertPtr v = dl.VtxBuffer[i];
            float t = Math.Clamp(Vector2.Dot(v.pos - p0, d) * invLengthSq, 0.0f, 1.0f);
            Vector4 c = Vector4.Lerp(c0, c1, t);
            c.W *= (v.col >> 24) / 255.0f;
            v.col = U32(c);
        }
    }

    private static Vector4 Rgb(int r, int g, int b) => new(r / 255.0f, g / 255.0f, b / 255.0f, 1.0f);

    private static Vector4 Lighten(Vector4 c, float t) => new(c.X + (1 - c.X) * t, c.Y + (1 - c.Y) * t, c.Z + (1 - c.Z) * t, c.W);

    private static Vector4 Darken(Vector4 c, float f) => new(c.X * f, c.Y * f, c.Z * f, c.W);

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    private static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
}
