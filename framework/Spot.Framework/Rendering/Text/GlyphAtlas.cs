namespace Spot.Rendering;

/// <summary>
/// A rectangle placed inside a glyph atlas, in texels.
/// </summary>
/// <param name="X">The left edge, in texels.</param>
/// <param name="Y">The top edge, in texels.</param>
/// <param name="Width">The width, in texels.</param>
/// <param name="Height">The height, in texels.</param>
public readonly record struct AtlasRect(int X, int Y, int Width, int Height);

/// <summary>
/// The result of packing glyph rectangles into an atlas: the final atlas size and the placement of
/// every input item, in the same order it was given.
/// </summary>
/// <param name="Width">The atlas width, in texels.</param>
/// <param name="Height">The atlas height, in texels.</param>
/// <param name="Rects">The placement of each input item, index-aligned with the input.</param>
public sealed record AtlasPacking(int Width, int Height, AtlasRect[] Rects);

/// <summary>
/// A tiny shelf packer for glyph bitmaps. It places rectangles left to right on rows ("shelves") of a
/// fixed width, opening a new row when the current one fills up. This is pure geometry with no GPU work,
/// so it can be unit tested on its own; <see cref="Font"/> uses it to lay out a font's glyphs before
/// uploading the atlas texture once.
/// </summary>
public static class GlyphAtlas
{
    /// <summary>
    /// Packs the given item sizes into an atlas of the given width, returning the atlas height and each
    /// item's placement. Items are sorted by height internally for a tighter pack, but the returned
    /// <see cref="AtlasPacking.Rects"/> stay index-aligned with <paramref name="items"/>. Zero-area items
    /// (for example the space glyph) take no room and are returned as empty rects.
    /// </summary>
    /// <param name="items">The width/height of each item to place, in texels.</param>
    /// <param name="atlasWidth">The fixed atlas width, in texels.</param>
    /// <param name="padding">The gap kept around each item, in texels, to stop bilinear filtering from
    /// bleeding neighboring glyphs into each other.</param>
    public static AtlasPacking Pack(ReadOnlySpan<(int Width, int Height)> items, int atlasWidth, int padding = 1)
    {
        int n = items.Length;
        var rects = new AtlasRect[n];

        // Place taller glyphs first so short ones fill the ragged ends of shelves.
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        var heights = new int[n];
        for (int i = 0; i < n; i++) heights[i] = items[i].Height;
        Array.Sort(order, (a, b) => heights[b].CompareTo(heights[a]));

        int penX = padding;
        int penY = padding;
        int shelfHeight = 0;
        int atlasHeight = padding;

        foreach (int idx in order)
        {
            (int w, int h) = items[idx];
            if (w <= 0 || h <= 0)
            {
                rects[idx] = new AtlasRect(0, 0, 0, 0);
                continue;
            }

            if (penX + w + padding > atlasWidth)
            {
                // Current shelf is full: drop down to a new one.
                penX = padding;
                penY += shelfHeight + padding;
                shelfHeight = 0;
            }

            rects[idx] = new AtlasRect(penX, penY, w, h);
            penX += w + padding;
            shelfHeight = Math.Max(shelfHeight, h);
            atlasHeight = Math.Max(atlasHeight, penY + h + padding);
        }

        return new AtlasPacking(atlasWidth, Math.Max(atlasHeight, padding), rects);
    }
}
