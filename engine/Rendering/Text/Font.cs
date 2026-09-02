using System.Numerics;
using Spot.Assets;
using StbTrueTypeSharp;

namespace Spot.Rendering;

/// <summary>
/// The measured shape of a single glyph, in atlas base pixels (see <see cref="IFontMetrics.BasePixelSize"/>).
/// All offsets are relative to the pen position sitting on the text baseline, with y pointing down.
/// </summary>
/// <param name="Codepoint">The Unicode code point this glyph renders.</param>
/// <param name="AdvanceX">How far the pen moves right after drawing this glyph.</param>
/// <param name="OffsetX">The glyph bitmap's left edge, relative to the pen.</param>
/// <param name="OffsetY">The glyph bitmap's top edge, relative to the baseline (negative is above it).</param>
/// <param name="Width">The glyph bitmap width. Zero for whitespace.</param>
/// <param name="Height">The glyph bitmap height. Zero for whitespace.</param>
/// <param name="Uv">The glyph's rectangle in the atlas as <c>(u0, v0, u1, v1)</c>, in 0..1 texture space.</param>
public readonly record struct GlyphInfo(
    int Codepoint,
    float AdvanceX,
    float OffsetX,
    float OffsetY,
    float Width,
    float Height,
    Vector4 Uv);

/// <summary>
/// The metrics a text layout needs, decoupled from any GPU resources so <see cref="TextLayout"/> can be
/// exercised without a graphics context. <see cref="Font"/> is the real implementation.
/// </summary>
public interface IFontMetrics
{
    /// <summary>The pixel size the glyphs were rasterized at. All returned metrics are in these units;
    /// callers scale by <c>targetSize / BasePixelSize</c> to draw at another size.</summary>
    float BasePixelSize { get; }

    /// <summary>The distance from one line's baseline to the next, in base pixels.</summary>
    float LineHeight { get; }

    /// <summary>The distance from the baseline up to the top of the tallest glyphs, in base pixels.</summary>
    float Ascent { get; }

    /// <summary>The distance from the baseline down to the lowest descenders, in base pixels (negative).</summary>
    float Descent { get; }

    /// <summary>Looks up a glyph by code point.</summary>
    /// <param name="codepoint">The Unicode code point.</param>
    /// <param name="glyph">The glyph metrics, when found.</param>
    /// <returns><c>true</c> if the font has this glyph baked; otherwise <c>false</c>.</returns>
    bool TryGetGlyph(int codepoint, out GlyphInfo glyph);

    /// <summary>The extra spacing to insert between a specific pair of code points, in base pixels (usually 0).</summary>
    /// <param name="left">The preceding code point.</param>
    /// <param name="right">The following code point.</param>
    float GetKerning(int left, int right);
}

/// <summary>
/// A TrueType/OpenType font rasterized to a single GPU atlas texture, drawn as batched quads. A fixed
/// set of code points (ASCII plus the Latin-1 supplement, which covers Western European text including
/// Portuguese accents) is baked once at load into one atlas at <see cref="BasePixelSize"/>; drawing at
/// other sizes scales the quads. This is deliberately independent of the editor's ImGui fonts, so game
/// UI and world text work in a shipped build.
/// </summary>
public sealed class Font : IFontMetrics, IDisposable
{
    /// <summary>The pixel height glyphs are rasterized at. Text near this size is crispest.</summary>
    public const float BasePixelSize = 48f;

    private const int AtlasWidth = 512;

    // ASCII printable + Latin-1 supplement (accented letters, symbols). Enough for Western European text.
    private static readonly (int Start, int End)[] BakedRanges = { (32, 126), (160, 255) };

    private readonly Dictionary<int, GlyphInfo> _glyphs = new();
    private readonly Dictionary<long, float> _kerning = new();
    private readonly Texture2D _atlas;

    private static Font? s_default;

    /// <summary>
    /// Builds a font from raw TrueType/OpenType bytes.
    /// </summary>
    /// <param name="ttf">The full contents of a <c>.ttf</c>/<c>.otf</c> file.</param>
    /// <param name="name">A display name for logging and lookup.</param>
    /// <exception cref="InvalidOperationException">The bytes could not be parsed as a font.</exception>
    public unsafe Font(byte[] ttf, string name)
    {
        Name = name;

        var info = new StbTrueType.stbtt_fontinfo();

        // stbtt keeps a pointer into the font bytes, so every glyph query and rasterization must happen
        // while the array stays pinned. Do all of it inside this fixed block.
        fixed (byte* data = ttf)
        {
            if (StbTrueType.stbtt_InitFont(info, data, 0) == 0)
            {
                throw new InvalidOperationException($"Failed to parse font '{name}'.");
            }

            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, BasePixelSize);

            int ascent, descent, lineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &lineGap);
            Ascent = ascent * scale;
            Descent = descent * scale;
            LineHeight = (ascent - descent + lineGap) * scale;

            var codepoints = new List<int>();
            foreach ((int start, int end) in BakedRanges)
            {
                for (int c = start; c <= end; c++) codepoints.Add(c);
            }

            int n = codepoints.Count;
            var sizes = new (int Width, int Height)[n];
            var boxes = new (int X0, int Y0)[n];
            var advances = new float[n];

            for (int i = 0; i < n; i++)
            {
                int cp = codepoints[i];

                int advance, leftBearing;
                StbTrueType.stbtt_GetCodepointHMetrics(info, cp, &advance, &leftBearing);
                advances[i] = advance * scale;

                int ix0, iy0, ix1, iy1;
                StbTrueType.stbtt_GetCodepointBitmapBox(info, cp, scale, scale, &ix0, &iy0, &ix1, &iy1);
                boxes[i] = (ix0, iy0);
                sizes[i] = (ix1 - ix0, iy1 - iy0);
            }

            AtlasPacking packing = GlyphAtlas.Pack(sizes, AtlasWidth, padding: 1);
            int atlasW = packing.Width;
            int atlasH = packing.Height;

            // RGB is white everywhere and only alpha carries glyph coverage, so bilinear filtering at the
            // transparent edges blends alpha (not color) and text keeps clean edges without a dark fringe.
            var buffer = new byte[atlasW * atlasH * 4];
            for (int i = 0; i < buffer.Length; i += 4)
            {
                buffer[i] = 255;
                buffer[i + 1] = 255;
                buffer[i + 2] = 255;
                buffer[i + 3] = 0;
            }

            for (int i = 0; i < n; i++)
            {
                (int w, int h) = sizes[i];
                AtlasRect r = packing.Rects[i];

                if (w > 0 && h > 0)
                {
                    var coverage = new byte[w * h];
                    fixed (byte* dst = coverage)
                    {
                        StbTrueType.stbtt_MakeCodepointBitmap(info, dst, w, h, w, scale, scale, codepoints[i]);
                    }

                    for (int gy = 0; gy < h; gy++)
                    {
                        int dstRow = ((r.Y + gy) * atlasW + r.X) * 4;
                        int srcRow = gy * w;
                        for (int gx = 0; gx < w; gx++)
                        {
                            buffer[dstRow + gx * 4 + 3] = coverage[srcRow + gx];
                        }
                    }
                }

                var uv = new Vector4(
                    r.X / (float)atlasW,
                    r.Y / (float)atlasH,
                    (r.X + w) / (float)atlasW,
                    (r.Y + h) / (float)atlasH);

                _glyphs[codepoints[i]] = new GlyphInfo(codepoints[i], advances[i], boxes[i].X0, boxes[i].Y0, w, h, uv);
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    int kern = StbTrueType.stbtt_GetCodepointKernAdvance(info, codepoints[i], codepoints[j]);
                    if (kern != 0)
                    {
                        _kerning[KerningKey(codepoints[i], codepoints[j])] = kern * scale;
                    }
                }
            }

            _atlas = new Texture2D((uint)atlasW, (uint)atlasH, buffer, pointFilter: false);
        }
    }

    /// <summary>Gets the font's display name.</summary>
    public string Name { get; }

    float IFontMetrics.BasePixelSize => BasePixelSize;

    /// <inheritdoc />
    public float LineHeight { get; }

    /// <inheritdoc />
    public float Ascent { get; }

    /// <inheritdoc />
    public float Descent { get; }

    /// <summary>The atlas texture holding every baked glyph. Bound by the renderer when drawing text.</summary>
    public Texture2D Atlas => _atlas;

    /// <summary>
    /// The engine's built-in default font (Inter Medium), embedded in the engine assembly and rasterized on
    /// first use, so in-game text renders even when a project ships no font of its own. Created lazily on the
    /// render thread.
    /// </summary>
    public static Font Default
    {
        get
        {
            if (s_default is not null) return s_default;

            using Stream? stream = typeof(Font).Assembly.GetManifestResourceStream("Spot.DefaultFont.ttf")
                ?? throw new InvalidOperationException("Embedded default font resource is missing.");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            s_default = new Font(ms.ToArray(), "Default");
            return s_default;
        }
    }

    /// <summary>
    /// Loads a font from a stored reference: a <c>guid:</c> reference resolves to its cooked
    /// <c>.sptfont</c> through the content host, while any other value is loaded as a source
    /// <c>.ttf</c>/<c>.otf</c> path. This mirrors <see cref="Texture2D.Load"/> and <c>AudioClip.Load</c>,
    /// so callers never care whether the project has been cooked.
    /// </summary>
    /// <param name="storedRef">A <c>guid:</c> reference or a source font path.</param>
    /// <exception cref="FileNotFoundException">A <c>guid:</c> reference has no cooked artifact.</exception>
    public static Font Load(string storedRef)
    {
        if (AssetRef.IsGuidRef(storedRef))
        {
            string cooked = AssetPath.ResolveContent(storedRef)
                ?? throw new FileNotFoundException($"Unresolved font reference '{storedRef}'.");
            SpFontData font = SpFont.ReadFile(cooked);
            return new Font(font.Ttf, font.Name);
        }

        string path = AssetPath.Resolve(storedRef);
        return new Font(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
    }

    /// <inheritdoc />
    public bool TryGetGlyph(int codepoint, out GlyphInfo glyph) => _glyphs.TryGetValue(codepoint, out glyph);

    /// <inheritdoc />
    public float GetKerning(int left, int right) =>
        _kerning.TryGetValue(KerningKey(left, right), out float k) ? k : 0f;

    /// <inheritdoc />
    public void Dispose() => _atlas.Dispose();

    private static long KerningKey(int left, int right) => ((long)left << 21) | (uint)right;
}
