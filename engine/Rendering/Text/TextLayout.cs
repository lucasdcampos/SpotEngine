using System.Numerics;

namespace Spot.Rendering;

/// <summary>How lines of text are positioned horizontally within their box.</summary>
public enum TextAlign
{
    /// <summary>Lines start at the left edge.</summary>
    Left,

    /// <summary>Lines are centered.</summary>
    Center,

    /// <summary>Lines end at the right edge.</summary>
    Right,
}

/// <summary>A single glyph placed by <see cref="TextLayout"/>, ready to emit as a textured quad.</summary>
/// <param name="Position">The top-left corner of the glyph quad, in the layout's local pixel space (y down).</param>
/// <param name="Size">The glyph quad's width and height, in pixels.</param>
/// <param name="Uv">The glyph's atlas rectangle as <c>(u0, v0, u1, v1)</c>.</param>
public readonly record struct PositionedGlyph(Vector2 Position, Vector2 Size, Vector4 Uv);

/// <summary>Options controlling how a string is laid out.</summary>
public struct TextLayoutOptions
{
    /// <summary>The wrap width in pixels. Zero or infinity disables wrapping (text stays on explicit lines).</summary>
    public float MaxWidth;

    /// <summary>Horizontal alignment of each line within the box.</summary>
    public TextAlign Align;

    /// <summary>A multiplier on the font's line height (1 = single-spaced).</summary>
    public float LineSpacing;

    /// <summary>The default options: no wrapping, left-aligned, single-spaced.</summary>
    public static TextLayoutOptions Default => new() { MaxWidth = 0f, Align = TextAlign.Left, LineSpacing = 1f };
}

/// <summary>
/// Turns a string into positioned glyph quads for a given <see cref="IFontMetrics"/> and pixel size. This is
/// pure geometry — measurement, word wrapping and alignment — with no GPU work, so it is unit tested directly
/// and shared by both the screen-space UI text and world-space text. Callers pass a reusable list to fill so
/// the per-frame render path allocates nothing.
/// </summary>
public static class TextLayout
{
    /// <summary>
    /// Lays out <paramref name="text"/> and, when <paramref name="output"/> is given, fills it with one
    /// <see cref="PositionedGlyph"/> per visible glyph (whitespace produces none). Positions are in local
    /// pixel space with the origin at the block's top-left and y pointing down.
    /// </summary>
    /// <param name="font">The font metrics to lay out with.</param>
    /// <param name="text">The string to lay out.</param>
    /// <param name="size">The target pixel size to render at.</param>
    /// <param name="options">Wrapping, alignment and spacing options.</param>
    /// <param name="output">A list to receive the placed glyphs; cleared first. Pass <c>null</c> to measure only.</param>
    /// <returns>The overall size of the laid-out block in pixels (box width when wrapping, else content width).</returns>
    public static Vector2 Build(IFontMetrics font, string text, float size, TextLayoutOptions options, List<PositionedGlyph>? output)
    {
        output?.Clear();
        if (string.IsNullOrEmpty(text) || size <= 0f || font.BasePixelSize <= 0f)
        {
            return Vector2.Zero;
        }

        float f = size / font.BasePixelSize;
        float lineSpacing = options.LineSpacing > 0f ? options.LineSpacing : 1f;
        float lineAdvance = font.LineHeight * f * lineSpacing;
        float ascent = font.Ascent * f;
        bool wrap = options.MaxWidth > 0f && !float.IsInfinity(options.MaxWidth);
        float spaceAdvance = font.TryGetGlyph(' ', out GlyphInfo space) ? space.AdvanceX : font.BasePixelSize * 0.3f;

        List<(int Start, int Length)> lines = SplitLines(font, text, f, wrap ? options.MaxWidth : 0f, spaceAdvance);

        // Measure every line first so alignment has a stable box width to work against.
        var widths = new float[lines.Count];
        float contentWidth = 0f;
        for (int i = 0; i < lines.Count; i++)
        {
            widths[i] = MeasureRange(font, text, lines[i].Start, lines[i].Length, f, spaceAdvance);
            contentWidth = MathF.Max(contentWidth, widths[i]);
        }

        float boxWidth = wrap ? options.MaxWidth : contentWidth;

        float y = 0f;
        for (int i = 0; i < lines.Count; i++)
        {
            float x = AlignOffset(options.Align, boxWidth, widths[i]);
            float baseline = y + ascent;
            if (output is not null)
            {
                EmitRange(font, text, lines[i].Start, lines[i].Length, f, x, baseline, spaceAdvance, output);
            }

            y += lineAdvance;
        }

        return new Vector2(boxWidth, lines.Count * lineAdvance);
    }

    /// <summary>Measures the block size of <paramref name="text"/> without producing glyphs.</summary>
    /// <param name="font">The font metrics to measure with.</param>
    /// <param name="text">The string to measure.</param>
    /// <param name="size">The target pixel size.</param>
    /// <param name="options">Wrapping, alignment and spacing options.</param>
    /// <returns>The block size in pixels.</returns>
    public static Vector2 Measure(IFontMetrics font, string text, float size, TextLayoutOptions options) =>
        Build(font, text, size, options, null);

    private static float AlignOffset(TextAlign align, float boxWidth, float lineWidth) => align switch
    {
        TextAlign.Center => (boxWidth - lineWidth) * 0.5f,
        TextAlign.Right => boxWidth - lineWidth,
        _ => 0f,
    };

    private static float Advance(IFontMetrics font, int codepoint, float spaceAdvance) =>
        font.TryGetGlyph(codepoint, out GlyphInfo g) ? g.AdvanceX : spaceAdvance;

    private static float MeasureRange(IFontMetrics font, string text, int start, int length, float f, float spaceAdvance)
    {
        float pen = 0f;
        int end = start + length;
        for (int i = start; i < end; i++)
        {
            pen += Advance(font, text[i], spaceAdvance) * f;
            if (i + 1 < end)
            {
                pen += font.GetKerning(text[i], text[i + 1]) * f;
            }
        }

        return pen;
    }

    private static void EmitRange(
        IFontMetrics font, string text, int start, int length, float f, float x, float baseline, float spaceAdvance, List<PositionedGlyph> output)
    {
        float pen = x;
        int end = start + length;
        for (int i = start; i < end; i++)
        {
            int cp = text[i];
            if (font.TryGetGlyph(cp, out GlyphInfo g))
            {
                if (g.Width > 0f && g.Height > 0f)
                {
                    var position = new Vector2(pen + g.OffsetX * f, baseline + g.OffsetY * f);
                    var glyphSize = new Vector2(g.Width * f, g.Height * f);
                    output.Add(new PositionedGlyph(position, glyphSize, g.Uv));
                }

                pen += g.AdvanceX * f;
            }
            else
            {
                pen += spaceAdvance * f;
            }

            if (i + 1 < end)
            {
                pen += font.GetKerning(cp, text[i + 1]) * f;
            }
        }
    }

    // Splits text into line ranges, honoring explicit '\n' and greedily word-wrapping at spaces when a wrap
    // width is set. Kerning is ignored while wrapping (a sub-pixel simplification); the final measure/emit
    // passes account for it.
    private static List<(int Start, int Length)> SplitLines(IFontMetrics font, string text, float f, float maxWidth, float spaceAdvance)
    {
        var lines = new List<(int Start, int Length)>();
        bool wrap = maxWidth > 0f;

        int lineStart = 0;
        int lastSpace = -1;
        float lineWidth = 0f;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '\n')
            {
                lines.Add((lineStart, i - lineStart));
                lineStart = i + 1;
                lastSpace = -1;
                lineWidth = 0f;
                i++;
                continue;
            }

            float advance = Advance(font, c, spaceAdvance) * f;

            // A space is a break opportunity, never a break trigger: absorb it (even past the edge, so a
            // trailing space can overflow) and let the next word decide whether to wrap here.
            if (c == ' ')
            {
                lastSpace = i;
                lineWidth += advance;
                i++;
                continue;
            }

            if (wrap && lineWidth + advance > maxWidth && i > lineStart)
            {
                if (lastSpace >= lineStart)
                {
                    // Break after the last space; the space itself is dropped from the wrapped line.
                    lines.Add((lineStart, lastSpace - lineStart));
                    lineStart = lastSpace + 1;
                }
                else
                {
                    // A single word wider than the box: hard-break before the current character.
                    lines.Add((lineStart, i - lineStart));
                    lineStart = i;
                }

                lastSpace = -1;
                lineWidth = MeasureRange(font, text, lineStart, i - lineStart, f, spaceAdvance);
                continue;
            }

            lineWidth += advance;
            i++;
        }

        lines.Add((lineStart, text.Length - lineStart));
        return lines;
    }
}
