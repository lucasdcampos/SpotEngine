using System.Numerics;
using Spot.Rendering;

namespace Spot.Engine.Tests;

public class TextLayoutTests
{
    // A deterministic, GL-free font: every glyph is 8x8 and advances 10, except the space which draws
    // nothing and advances 6. Base pixel size 10, so size 10 means scale 1.
    private sealed class FakeFont : IFontMetrics
    {
        public float BasePixelSize => 10f;
        public float LineHeight => 12f;
        public float Ascent => 8f;
        public float Descent => -2f;

        public bool TryGetGlyph(int codepoint, out GlyphInfo glyph)
        {
            glyph = codepoint == ' '
                ? new GlyphInfo(' ', 6f, 0f, 0f, 0f, 0f, Vector4.Zero)
                : new GlyphInfo(codepoint, 10f, 1f, -8f, 8f, 8f, Vector4.Zero);
            return true;
        }

        public float GetKerning(int left, int right) => 0f;
    }

    private static readonly FakeFont Font = new();

    [Fact]
    public void Measure_SingleLine_IsAdvanceWidthByLineHeight()
    {
        Vector2 size = TextLayout.Measure(Font, "AAAA", 10f, TextLayoutOptions.Default);

        Assert.Equal(40f, size.X, 3);
        Assert.Equal(12f, size.Y, 3);
    }

    [Fact]
    public void Build_PlacesGlyphsOnBaselineLeftToRight()
    {
        var glyphs = new List<PositionedGlyph>();
        TextLayout.Build(Font, "AB", 10f, TextLayoutOptions.Default, glyphs);

        Assert.Equal(2, glyphs.Count);
        // baseline = ascent (8); glyph top = baseline + offsetY (-8) = 0; x = pen + offsetX (1).
        Assert.Equal(new Vector2(1f, 0f), glyphs[0].Position);
        Assert.Equal(new Vector2(11f, 0f), glyphs[1].Position);
        Assert.Equal(new Vector2(8f, 8f), glyphs[0].Size);
    }

    [Fact]
    public void Build_SkipsWhitespaceGlyphsButStillAdvances()
    {
        var glyphs = new List<PositionedGlyph>();
        Vector2 size = TextLayout.Build(Font, "A A", 10f, TextLayoutOptions.Default, glyphs);

        Assert.Equal(2, glyphs.Count);           // two 'A's, the space draws nothing
        Assert.Equal(26f, size.X, 3);            // 10 + 6 + 10
        Assert.Equal(new Vector2(17f, 0f), glyphs[1].Position); // pen after "A " = 16, + offsetX 1
    }

    [Fact]
    public void Build_ExplicitNewlineStartsANewLine()
    {
        var glyphs = new List<PositionedGlyph>();
        Vector2 size = TextLayout.Build(Font, "A\nB", 10f, TextLayoutOptions.Default, glyphs);

        Assert.Equal(2, glyphs.Count);
        Assert.Equal(24f, size.Y, 3);                // two lines * 12
        Assert.Equal(0f, glyphs[0].Position.Y, 3);   // first line baseline top
        Assert.Equal(12f, glyphs[1].Position.Y, 3);  // second line dropped by one line height
    }

    [Fact]
    public void Build_WrapsAtSpacesWithinMaxWidth()
    {
        var options = TextLayoutOptions.Default;
        options.MaxWidth = 25f;
        var glyphs = new List<PositionedGlyph>();

        Vector2 size = TextLayout.Build(Font, "AA AA", 10f, options, glyphs);

        Assert.Equal(25f, size.X, 3);   // box width
        Assert.Equal(24f, size.Y, 3);   // wrapped onto two lines
        // The second "AA" starts a fresh line, so its first glyph sits on the second baseline.
        Assert.Equal(12f, glyphs[2].Position.Y, 3);
    }

    [Fact]
    public void Build_HardWrapsAWordWiderThanTheBox()
    {
        var options = TextLayoutOptions.Default;
        options.MaxWidth = 25f;
        var glyphs = new List<PositionedGlyph>();

        Vector2 size = TextLayout.Build(Font, "AAAAAA", 10f, options, glyphs);

        Assert.Equal(36f, size.Y, 3);   // 6 glyphs of width 10 in a 25px box -> 3 lines
        Assert.Equal(6, glyphs.Count);
    }

    [Fact]
    public void Build_CenterAlignsShorterLinesWithinTheBox()
    {
        var options = TextLayoutOptions.Default;
        options.MaxWidth = 100f;
        options.Align = TextAlign.Center;
        var glyphs = new List<PositionedGlyph>();

        TextLayout.Build(Font, "AA", 10f, options, glyphs);

        // line width 20 in a 100px box -> (100-20)/2 = 40 offset, + glyph offsetX 1.
        Assert.Equal(41f, glyphs[0].Position.X, 3);
    }

    [Fact]
    public void Build_EmptyStringProducesNothing()
    {
        var glyphs = new List<PositionedGlyph>();
        Vector2 size = TextLayout.Build(Font, "", 10f, TextLayoutOptions.Default, glyphs);

        Assert.Empty(glyphs);
        Assert.Equal(Vector2.Zero, size);
    }
}
