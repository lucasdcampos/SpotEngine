using System.Numerics;

namespace Voxelcraft;

/// <summary>The atlas tiles, numbered left to right, top to bottom, 16 to a row.</summary>
public static class Tiles
{
    public const int GrassTop = 0;
    public const int GrassSide = 1;
    public const int Dirt = 2;
    public const int Stone = 3;
    public const int Cobblestone = 4;
    public const int Sand = 5;
    public const int Gravel = 6;
    public const int OakLog = 7;
    public const int OakLogTop = 8;
    public const int OakLeaves = 9;
    public const int Planks = 10;
    public const int Water = 11;
    public const int Snow = 12;
    public const int SnowySide = 13;
    public const int Bedrock = 14;
    public const int CoalOre = 15;
    public const int IronOre = 16;
    public const int GoldOre = 17;
    public const int DiamondOre = 18;
    public const int TallGrass = 19;
    public const int Poppy = 20;
    public const int Dandelion = 21;
    public const int DeadBush = 22;
    public const int Glass = 23;
    public const int Bricks = 24;
    public const int Glowstone = 25;
    public const int Sandstone = 26;
    public const int SandstoneTop = 27;
    public const int Cactus = 28;
    public const int CactusTop = 29;
    public const int SpruceLog = 30;
    public const int SpruceLogTop = 31;
    public const int SpruceLeaves = 32;
    public const int BirchLog = 33;
    public const int BirchLogTop = 34;
    public const int BirchLeaves = 35;
}

/// <summary>
/// Paints every block texture at startup — 16×16 pixel art from hashed noise — into one 256×256 atlas, plus a
/// second atlas that marks the texels the biome's foliage color tints (grass, leaves, stems). The sample ships no
/// image files: this is all of its art, and it also draws the isometric block icons the hotbar shows.
/// </summary>
public static class BlockAtlas
{
    public const int TileSize = 16;
    public const int Columns = 16;
    public const int Size = TileSize * Columns;

    /// <summary>The foliage color used where no biome applies (the icons).</summary>
    public static readonly Vector3 DefaultFoliage = new(0.50f, 0.76f, 0.32f);

    /// <summary>Gets the atlas, RGBA, rows top to bottom.</summary>
    public static byte[] Pixels { get; private set; } = Array.Empty<byte>();

    /// <summary>Gets the tint mask atlas: 255 where the foliage color applies.</summary>
    public static byte[] TintMask { get; private set; } = Array.Empty<byte>();

    private static int s_tile;

    public static void Build()
    {
        if (Pixels.Length > 0) return;

        Pixels = new byte[Size * Size * 4];
        TintMask = new byte[Size * Size * 4];

        Paint(Tiles.Dirt, Dirt);
        Paint(Tiles.GrassTop, (x, y) => Foliage(Grassy(x, y)));
        Paint(Tiles.GrassSide, GrassSide);
        Paint(Tiles.Stone, Stone);
        Paint(Tiles.Cobblestone, Cobble);
        Paint(Tiles.Sand, (x, y) => Speckle(x, y, Rgb(219, 206, 160), 0.06f, 0.05f));
        Paint(Tiles.Gravel, Gravel);
        Paint(Tiles.OakLog, (x, y) => Bark(x, y, Rgb(104, 82, 50), Rgb(74, 57, 34)));
        Paint(Tiles.OakLogTop, (x, y) => Rings(x, y, Rgb(176, 141, 88), Rgb(140, 108, 62), Rgb(104, 82, 50)));
        Paint(Tiles.OakLeaves, (x, y) => Leaves(x, y, null, 0.22f));
        Paint(Tiles.Planks, Planks);
        Paint(Tiles.Water, (x, y) => Speckle(x, y, Rgb(52, 98, 196), 0.05f, 0.0f));
        Paint(Tiles.Snow, (x, y) => Speckle(x, y, Rgb(243, 250, 255), 0.025f, 0.0f));
        Paint(Tiles.SnowySide, SnowySide);
        Paint(Tiles.Bedrock, (x, y) => Speckle(x, y, Rgb(84, 84, 86), 0.45f, 0.1f));
        Paint(Tiles.CoalOre, (x, y) => Ore(x, y, Rgb(38, 38, 40), Rgb(20, 20, 22), 11));
        Paint(Tiles.IronOre, (x, y) => Ore(x, y, Rgb(216, 175, 147), Rgb(170, 125, 96), 23));
        Paint(Tiles.GoldOre, (x, y) => Ore(x, y, Rgb(252, 238, 75), Rgb(220, 170, 30), 37));
        Paint(Tiles.DiamondOre, (x, y) => Ore(x, y, Rgb(112, 236, 228), Rgb(40, 170, 170), 41));
        Paint(Tiles.TallGrass, TallGrass);
        Paint(Tiles.Poppy, (x, y) => Flower(x, y, Rgb(218, 36, 30), Rgb(150, 20, 18), 8));
        Paint(Tiles.Dandelion, (x, y) => Flower(x, y, Rgb(255, 222, 40), Rgb(225, 170, 20), 9));
        Paint(Tiles.DeadBush, DeadBush);
        Paint(Tiles.Glass, Glass);
        Paint(Tiles.Bricks, Bricks);
        Paint(Tiles.Glowstone, Glowstone);
        Paint(Tiles.Sandstone, Sandstone);
        Paint(Tiles.SandstoneTop, (x, y) => Speckle(x, y, Rgb(222, 208, 156), 0.035f, 0.0f));
        Paint(Tiles.Cactus, Cactus);
        Paint(Tiles.CactusTop, CactusTop);
        Paint(Tiles.SpruceLog, (x, y) => Bark(x, y, Rgb(68, 48, 30), Rgb(46, 32, 20)));
        Paint(Tiles.SpruceLogTop, (x, y) => Rings(x, y, Rgb(126, 96, 58), Rgb(100, 74, 44), Rgb(68, 48, 30)));
        Paint(Tiles.SpruceLeaves, (x, y) => Leaves(x, y, Rgb(54, 92, 60), 0.16f));
        Paint(Tiles.BirchLog, BirchBark);
        Paint(Tiles.BirchLogTop, (x, y) => Rings(x, y, Rgb(206, 186, 130), Rgb(176, 154, 102), Rgb(220, 220, 212)));
        Paint(Tiles.BirchLeaves, (x, y) => Leaves(x, y, Rgb(118, 160, 72), 0.2f));
    }

    /// <summary>Gets the RGBA of a texel of a tile, its tint mask applied with <paramref name="foliage"/>.</summary>
    public static Vector4 Sample(int tile, int x, int y, Vector3 foliage)
    {
        int px = (tile % Columns) * TileSize + Math.Clamp(x, 0, TileSize - 1);
        int py = (tile / Columns) * TileSize + Math.Clamp(y, 0, TileSize - 1);
        int i = (py * Size + px) * 4;
        var color = new Vector4(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]) / 255.0f;
        if (TintMask[i] > 127)
        {
            color = new Vector4(color.X * foliage.X, color.Y * foliage.Y, color.Z * foliage.Z, color.W);
        }

        return color;
    }

    /// <summary>The average color of a tile (particles, the map), tinted where its mask says.</summary>
    public static Vector3 Average(int tile, Vector3 foliage)
    {
        Vector3 sum = Vector3.Zero;
        float weight = 0.0f;
        for (int y = 0; y < TileSize; y++)
        {
            for (int x = 0; x < TileSize; x++)
            {
                Vector4 c = Sample(tile, x, y, foliage);
                sum += new Vector3(c.X, c.Y, c.Z) * c.W;
                weight += c.W;
            }
        }

        return weight > 0.0f ? sum / weight : Vector3.One;
    }

    /// <summary>
    /// Draws an isometric icon of a block — its top and two sides, shaded like daylight — or a flat sprite for a
    /// plant, into an RGBA buffer (rows top to bottom).
    /// </summary>
    public static void DrawIcon(BlockId id, byte[] target, int stride, int ox, int oy, int size)
    {
        BlockInfo info = Blocks.Get(id);
        if (info.Shape == BlockShape.Plant)
        {
            int inset = size / 8;
            for (int y = 0; y < size - inset * 2; y++)
            {
                for (int x = 0; x < size - inset * 2; x++)
                {
                    Vector4 c = Sample(info.Side, x * TileSize / (size - inset * 2), y * TileSize / (size - inset * 2), DefaultFoliage);
                    Put(target, stride, ox + inset + x, oy + inset + y, c);
                }
            }

            return;
        }

        // Pixel space: the top rhombus, then the left and right faces below it.
        float s = size;
        var top = new Vector2(s * 0.5f, s * 0.04f);
        var left = new Vector2(s * 0.06f, s * 0.27f);
        var right = new Vector2(s * 0.94f, s * 0.27f);
        var center = new Vector2(s * 0.5f, s * 0.5f);
        var down = new Vector2(0.0f, s * 0.46f);
        Face(info.Top, left, top - left, center - left, 1.0f);           // u along the back-left edge, v toward the front
        Face(info.Side, left, center - left, down, 0.78f);               // the left face
        Face(info.Side, center, right - center, down, 0.6f);             // the right face

        void Face(int tile, Vector2 origin, Vector2 u, Vector2 v, float shade)
        {
            float det = u.X * v.Y - u.Y * v.X;
            if (MathF.Abs(det) < 1e-5f) return;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - origin;
                    float a = (p.X * v.Y - p.Y * v.X) / det;
                    float b = (u.X * p.Y - u.Y * p.X) / det;
                    if (a < 0.0f || a >= 1.0f || b < 0.0f || b >= 1.0f) continue;
                    Vector4 c = Sample(tile, (int)(a * TileSize), (int)(b * TileSize), DefaultFoliage);
                    if (c.W < 0.5f) continue;
                    float glow = info.Emissive ? 1.0f : shade;
                    Put(target, stride, ox + x, oy + y, new Vector4(c.X * glow, c.Y * glow, c.Z * glow, 1.0f));
                }
            }
        }
    }

    private static void Put(byte[] target, int stride, int x, int y, Vector4 c)
    {
        if (c.W <= 0.01f || x < 0 || y < 0 || x >= stride || (y * stride + x) * 4 + 3 >= target.Length) return;
        int i = (y * stride + x) * 4;
        target[i] = (byte)Math.Clamp(c.X * 255.0f, 0.0f, 255.0f);
        target[i + 1] = (byte)Math.Clamp(c.Y * 255.0f, 0.0f, 255.0f);
        target[i + 2] = (byte)Math.Clamp(c.Z * 255.0f, 0.0f, 255.0f);
        target[i + 3] = 255;
    }

    // ---- painting ----

    // A texel: its color (sRGB, 0..1, alpha) and whether the foliage color tints it.
    private readonly record struct Texel(Vector4 Color, bool Tint);

    private static void Paint(int tile, Func<int, int, Texel> painter)
    {
        s_tile = tile;
        int ox = (tile % Columns) * TileSize;
        int oy = (tile / Columns) * TileSize;
        for (int y = 0; y < TileSize; y++)
        {
            for (int x = 0; x < TileSize; x++)
            {
                Texel t = painter(x, y);
                int i = ((oy + y) * Size + ox + x) * 4;
                Pixels[i] = ToByte(t.Color.X);
                Pixels[i + 1] = ToByte(t.Color.Y);
                Pixels[i + 2] = ToByte(t.Color.Z);
                Pixels[i + 3] = ToByte(t.Color.W);
                byte mask = t.Tint ? (byte)255 : (byte)0;
                TintMask[i] = mask;
                TintMask[i + 1] = mask;
                TintMask[i + 2] = mask;
                TintMask[i + 3] = 255;
            }
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255.0f), 0.0f, 255.0f);

    private static Vector3 Rgb(int r, int g, int b) => new(r / 255.0f, g / 255.0f, b / 255.0f);

    private static Texel Solid(Vector3 c) => new(new Vector4(c, 1.0f), false);

    private static Texel Foliage(float gray) => new(new Vector4(gray, gray, gray, 1.0f), true);

    private static Texel Clear => new(Vector4.Zero, false);

    // A hash of the texel and the tile being painted: 0..1.
    private static float Hash(int x, int y, int salt = 0)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + (s_tile + 1) * 1274126177 + salt * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    // Value noise that wraps every 16 texels, so tiles repeat seamlessly.
    private static float Smooth(float x, float y, int period, int salt)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;
        fx = fx * fx * (3.0f - 2.0f * fx);
        fy = fy * fy * (3.0f - 2.0f * fy);
        float Corner(int cx, int cy) => Hash(((cx % period) + period) % period, ((cy % period) + period) % period, salt);
        float a = Corner(x0, y0) + (Corner(x0 + 1, y0) - Corner(x0, y0)) * fx;
        float b = Corner(x0, y0 + 1) + (Corner(x0 + 1, y0 + 1) - Corner(x0, y0 + 1)) * fx;
        return a + (b - a) * fy;
    }

    private static Vector3 Vary(Vector3 c, float amount, int x, int y, int salt = 0) =>
        c * (1.0f + (Hash(x, y, salt) - 0.5f) * 2.0f * amount);

    private static Texel Speckle(int x, int y, Vector3 color, float amount, float blotch)
    {
        float b = blotch > 0.0f ? (Smooth(x / 4.0f, y / 4.0f, 4, 7) - 0.5f) * 2.0f * blotch : 0.0f;
        return Solid(Vary(color, amount, x, y) * (1.0f + b));
    }

    private static float Grassy(int x, int y) => 0.64f + (Hash(x, y) - 0.5f) * 0.2f + (Smooth(x / 4.0f, y / 4.0f, 4, 3) - 0.5f) * 0.12f;

    private static Texel Dirt(int x, int y)
    {
        Vector3 c = Vary(Rgb(134, 96, 67), 0.1f, x, y);
        if (Hash(x, y, 5) > 0.88f) c = Rgb(108, 76, 52);
        if (Hash(x, y, 9) > 0.95f) c = Rgb(156, 118, 86);
        return Solid(c);
    }

    private static Texel GrassSide(int x, int y)
    {
        int fringe = 3 + (int)(Hash(x, 0, 11) * 2.4f);
        return y < fringe ? Foliage(Grassy(x, y) * 0.92f) : Dirt(x, y);
    }

    private static Texel SnowySide(int x, int y)
    {
        int fringe = 3 + (int)(Hash(x, 0, 12) * 2.4f);
        return y < fringe ? Solid(Vary(Rgb(240, 248, 255), 0.03f, x, y)) : Dirt(x, y);
    }

    private static Texel Stone(int x, int y)
    {
        float n = Smooth(x / 4.0f, y / 3.0f, 4, 1) * 0.6f + Smooth(x / 2.0f, y / 2.0f, 8, 2) * 0.4f;
        float v = 0.47f + (n - 0.5f) * 0.16f + (Hash(x, y) - 0.5f) * 0.06f;
        if (Hash(x, y, 4) > 0.93f) v -= 0.07f;
        return Solid(new Vector3(v, v, v * 1.02f));
    }

    // Cobblestone: rounded stones (nearest of a few wrapped seed points) with dark seams between them.
    private static Texel Cobble(int x, int y)
    {
        Span<Vector2> seeds = stackalloc Vector2[]
        {
            new(2, 2), new(9, 1), new(14, 5), new(5, 7), new(11, 9), new(2, 12), new(8, 14), new(14, 13),
        };
        float best = float.MaxValue;
        float second = float.MaxValue;
        int cell = 0;
        for (int i = 0; i < seeds.Length; i++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), seeds[i] + new Vector2(ox * 16, oy * 16));
                    if (d < best)
                    {
                        second = best;
                        best = d;
                        cell = i;
                    }
                    else if (d < second)
                    {
                        second = d;
                    }
                }
            }
        }

        if (second - best < 1.1f) return Solid(Vary(new Vector3(0.28f), 0.1f, x, y));
        float v = 0.5f + (cell % 3) * 0.05f - best * 0.025f + (Hash(x, y) - 0.5f) * 0.08f;
        return Solid(new Vector3(v, v, v));
    }

    private static Texel Gravel(int x, int y)
    {
        float h = Hash(x / 2, y / 2, 3);
        Vector3 c = h switch
        {
            < 0.3f => Rgb(122, 116, 112),
            < 0.55f => Rgb(150, 142, 138),
            < 0.75f => Rgb(104, 96, 92),
            < 0.9f => Rgb(136, 118, 104),
            _ => Rgb(176, 168, 162),
        };
        return Solid(Vary(c, 0.06f, x, y));
    }

    private static Texel Bark(int x, int y, Vector3 light, Vector3 dark)
    {
        float stripe = Smooth(x * 1.0f, y / 5.0f, 16, 4);
        Vector3 c = Vector3.Lerp(dark, light, stripe);
        if (Hash(x, y / 3, 6) > 0.85f) c = dark * 0.85f;
        return Solid(Vary(c, 0.05f, x, y));
    }

    private static Texel BirchBark(int x, int y)
    {
        Vector3 c = Vary(Rgb(218, 216, 208), 0.03f, x, y);
        // Dark horizontal dashes, the birch's signature.
        if (Hash(x / 3, y, 8) > 0.82f && Hash(x, y, 9) > 0.2f) c = Rgb(60, 58, 52);
        return Solid(c);
    }

    private static Texel Rings(int x, int y, Vector3 light, Vector3 dark, Vector3 bark)
    {
        if (x == 0 || y == 0 || x == 15 || y == 15) return Solid(Vary(bark, 0.06f, x, y));
        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(8.0f, 8.0f));
        float ring = MathF.Sin(d * 2.2f) * 0.5f + 0.5f;
        return Solid(Vary(Vector3.Lerp(dark, light, ring), 0.04f, x, y));
    }

    private static Texel Leaves(int x, int y, Vector3? color, float holes)
    {
        if (Hash(x, y, 13) < holes) return Clear;
        float v = 0.62f + (Hash(x, y) - 0.5f) * 0.3f + (Smooth(x / 3.0f, y / 3.0f, 5, 9) - 0.5f) * 0.2f;
        if (color is { } c) return Solid(c * (v / 0.62f));
        return Foliage(v);
    }

    private static Texel Planks(int x, int y)
    {
        int board = y / 4;
        int seam = (board % 2 == 0) ? 3 : 11;
        bool edge = y % 4 == 3 || x == seam;
        Vector3 c = Rgb(164, 132, 80) * (0.94f + Smooth(x / 6.0f, board * 3.0f, 3, board) * 0.12f);
        if (edge) c = Rgb(116, 90, 52);
        return Solid(Vary(c, 0.035f, x, y));
    }

    private static Texel Ore(int x, int y, Vector3 bright, Vector3 dark, int salt)
    {
        Texel stone = Stone(x, y);
        // Three clusters of ore, each a blob of 4-6 texels.
        Span<Vector2> centers = stackalloc Vector2[] { new(4, 4), new(11, 6), new(6, 12) };
        foreach (Vector2 c in centers)
        {
            float d = Vector2.Distance(new Vector2(x, y), c + new Vector2(Hash(salt, (int)c.X) * 2 - 1, Hash((int)c.Y, salt) * 2 - 1));
            if (d < 1.6f + Hash(x, y, salt) * 0.8f)
            {
                return Solid(Hash(x, y, salt + 1) > 0.5f ? bright : dark);
            }
        }

        return stone;
    }

    private static Texel TallGrass(int x, int y)
    {
        // Blades rising from the bottom edge to varied heights, leaning slightly.
        for (int blade = 0; blade < 7; blade++)
        {
            float bx = 1.5f + blade * 2.1f + Hash(blade, 1, 17) * 1.2f;
            float height = 6.0f + Hash(blade, 2, 17) * 9.0f;
            float lean = (Hash(blade, 3, 17) - 0.5f) * 0.5f;
            float t = (15.0f - y) / height;
            if (t < 0.0f || t > 1.0f) continue;
            float cx = bx + lean * (15.0f - y);
            if (MathF.Abs(x + 0.5f - cx) < 0.75f * (1.0f - t * 0.5f))
            {
                return Foliage(0.55f + t * 0.35f + (Hash(x, y) - 0.5f) * 0.1f);
            }
        }

        return Clear;
    }

    private static Texel Flower(int x, int y, Vector3 petal, Vector3 center, int stemTop)
    {
        // A stem with two leaves, crowned by a round bloom.
        if (x == 7 && y >= stemTop) return Foliage(0.6f);
        if ((y == 12 && x is 5 or 6) || (y == 11 && x is 9 or 10)) return Foliage(0.7f);
        var p = new Vector2(x + 0.5f, y + 0.5f);
        float d = Vector2.Distance(p, new Vector2(7.5f, stemTop - 2.5f));
        if (d < 1.2f) return Solid(center);
        if (d < 3.1f) return Solid(Vary(petal, 0.08f, x, y));
        return Clear;
    }

    private static Texel DeadBush(int x, int y)
    {
        Vector3 c = Rgb(124, 84, 40);
        // A trunk and forked twigs.
        if (x == 8 && y >= 9) return Solid(c);
        for (int branch = 0; branch < 5; branch++)
        {
            float dir = branch % 2 == 0 ? -1.0f : 1.0f;
            float baseY = 9.0f + Hash(branch, 1, 21) * 4.0f;
            float len = 4.0f + Hash(branch, 2, 21) * 4.0f;
            float t = (baseY - y) / len;
            if (t < 0.0f || t > 1.0f) continue;
            if (MathF.Abs(x - (8.0f + dir * t * len * (0.6f + branch * 0.1f))) < 0.6f) return Solid(c * (0.8f + t * 0.3f));
        }

        return Clear;
    }

    private static Texel Glass(int x, int y)
    {
        bool frame = x == 0 || y == 0 || x == 15 || y == 15;
        if (frame) return Solid(Vary(Rgb(200, 226, 232), 0.04f, x, y));
        // A few glints across the pane.
        if ((x == y + 3 && x is > 4 and < 9) || (x == y + 5 && x is > 7 and < 11) || (x == 3 && y == 2)) return Solid(Rgb(236, 248, 252));
        return Clear;
    }

    private static Texel Bricks(int x, int y)
    {
        int row = y / 4;
        int offset = row % 2 == 0 ? 0 : 4;
        bool mortar = y % 4 == 3 || (x + offset) % 8 == 7;
        if (mortar) return Solid(Vary(Rgb(176, 168, 158), 0.05f, x, y));
        int brick = ((x + offset) / 8) + row * 3;
        Vector3 c = Rgb(150, 74, 58) * (0.9f + Hash(brick, row, 31) * 0.2f);
        return Solid(Vary(c, 0.05f, x, y));
    }

    private static Texel Glowstone(int x, int y)
    {
        float n = Smooth(x / 3.0f, y / 3.0f, 5, 19);
        Vector3 c = Vector3.Lerp(Rgb(170, 110, 50), Rgb(255, 226, 150), n);
        if (Hash(x, y, 2) > 0.85f) c = Rgb(255, 244, 196);
        return Solid(c);
    }

    private static Texel Sandstone(int x, int y)
    {
        Vector3 c = Rgb(216, 200, 146);
        if (y < 3) c = Rgb(226, 212, 162);
        else if (y is 3 or 9) c = Rgb(190, 172, 120);
        else if (y > 11) c = Rgb(206, 188, 134);
        return Solid(Vary(c, 0.03f, x, y));
    }

    private static Texel Cactus(int x, int y)
    {
        Vector3 c = Rgb(72, 138, 52);
        if (x is 0 or 15) c = Rgb(44, 96, 34);
        else if (x % 4 == 2) c = Rgb(58, 118, 42);
        if ((x % 4 == 0) && Hash(x, y, 5) > 0.8f) c = Rgb(236, 230, 190);
        return Solid(Vary(c, 0.05f, x, y));
    }

    private static Texel CactusTop(int x, int y)
    {
        bool rim = x is 0 or 15 || y is 0 or 15;
        Vector3 c = rim ? Rgb(44, 96, 34) : Rgb(96, 160, 70);
        if (!rim && (x + y) % 5 == 0) c = Rgb(82, 146, 60);
        return Solid(Vary(c, 0.05f, x, y));
    }
}
