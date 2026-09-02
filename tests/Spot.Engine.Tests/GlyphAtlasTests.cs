using Spot.Rendering;

namespace Spot.Engine.Tests;

public class GlyphAtlasTests
{
    [Fact]
    public void Pack_KeepsEveryItemInsideTheAtlasWidth()
    {
        (int, int)[] items = { (30, 40), (50, 20), (10, 10), (120, 48), (5, 5) };

        AtlasPacking packing = GlyphAtlas.Pack(items, atlasWidth: 128, padding: 1);

        for (int i = 0; i < items.Length; i++)
        {
            AtlasRect r = packing.Rects[i];
            Assert.True(r.X >= 0 && r.X + r.Width <= packing.Width, $"item {i} overflows width");
            Assert.True(r.Y >= 0 && r.Y + r.Height <= packing.Height, $"item {i} overflows height");
        }
    }

    [Fact]
    public void Pack_DoesNotOverlapAnyTwoItems()
    {
        var items = new (int, int)[40];
        for (int i = 0; i < items.Length; i++) items[i] = (8 + i % 10, 12 + i % 6);

        AtlasPacking packing = GlyphAtlas.Pack(items, atlasWidth: 64, padding: 1);

        for (int a = 0; a < items.Length; a++)
        {
            for (int b = a + 1; b < items.Length; b++)
            {
                Assert.False(Overlaps(packing.Rects[a], packing.Rects[b]), $"items {a} and {b} overlap");
            }
        }
    }

    [Fact]
    public void Pack_TreatsZeroAreaItemsAsEmptyRects()
    {
        (int, int)[] items = { (0, 0), (10, 10), (0, 5) };

        AtlasPacking packing = GlyphAtlas.Pack(items, atlasWidth: 64);

        Assert.Equal(new AtlasRect(0, 0, 0, 0), packing.Rects[0]);
        Assert.Equal(new AtlasRect(0, 0, 0, 0), packing.Rects[2]);
        Assert.Equal(10, packing.Rects[1].Width);
    }

    private static bool Overlaps(AtlasRect a, AtlasRect b)
    {
        if (a.Width == 0 || a.Height == 0 || b.Width == 0 || b.Height == 0) return false;
        return a.X < b.X + b.Width && a.X + a.Width > b.X &&
               a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
    }
}
