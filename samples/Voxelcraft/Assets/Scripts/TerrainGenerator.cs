using System.Numerics;

namespace Voxelcraft;

public enum Biome
{
    Ocean,
    Beach,
    Plains,
    Forest,
    Desert,
    Snowy,
    Mountains,
}

/// <summary>What the generator decides for one column before placing blocks.</summary>
public readonly record struct Column(int Height, Biome Biome, float Temperature, float Humidity, float Mountain);

/// <summary>
/// The procedural world: a pure function of the seed and the block position, so any chunk can be generated on any
/// thread, in any order, and always comes out the same. Continents and oceans, rolling hills, ridged mountains with
/// snow caps, five biomes blended by temperature and humidity, winding tunnels and caverns, ores, trees that grow
/// across chunk borders, grass and flowers.
/// </summary>
public sealed class TerrainGenerator
{
    private readonly int _seed;
    private readonly Noise _continent;
    private readonly Noise _hills;
    private readonly Noise _ridges;
    private readonly Noise _mountainMask;
    private readonly Noise _detail;
    private readonly Noise _temperature;
    private readonly Noise _humidity;
    private readonly Noise _caveA;
    private readonly Noise _caveB;
    private readonly Noise _cavern;
    private readonly Noise _surface;

    public TerrainGenerator(int seed)
    {
        _seed = seed;
        _continent = new Noise(seed);
        _hills = new Noise(seed + 1);
        _ridges = new Noise(seed + 2);
        _mountainMask = new Noise(seed + 3);
        _detail = new Noise(seed + 4);
        _temperature = new Noise(seed + 5);
        _humidity = new Noise(seed + 6);
        _caveA = new Noise(seed + 7);
        _caveB = new Noise(seed + 8);
        _cavern = new Noise(seed + 9);
        _surface = new Noise(seed + 10);
    }

    public int Seed => _seed;

    /// <summary>Decides a column's surface height and biome.</summary>
    public Column ColumnAt(int x, int z)
    {
        float c = _continent.Fractal(x * 0.0011f, z * 0.0011f, 4) + 0.12f;
        float land = SmoothStep(-0.1f, 0.06f, c);

        float oceanFloor = MathF.Max(34.0f, 50.0f + c * 60.0f);
        float hill = _hills.Fractal(x * 0.0065f, z * 0.0065f, 4) * (5.0f + 12.0f * SmoothStep(0.0f, 0.45f, c));
        float landHeight = 64.0f + c * 22.0f + hill;

        float mask = SmoothStep(0.02f, 0.4f, _mountainMask.Fractal(x * 0.0013f + 91.0f, z * 0.0013f - 37.0f, 3));
        float ridge = _ridges.Ridged(x * 0.0042f, z * 0.0042f, 5);
        float mountain = mask * land * (ridge * 92.0f + 6.0f);

        float detail = _detail.Fractal(x * 0.04f, z * 0.04f, 2) * 1.6f;
        float height = Lerp(oceanFloor, landHeight, land) + mountain + detail;
        int h = Math.Clamp((int)MathF.Round(height), 8, Chunk.Height - 24);

        float temperature = _temperature.Fractal(x * 0.0009f, z * 0.0009f, 3) * 1.4f - MathF.Max(0.0f, h - 92) / 70.0f;
        float humidity = _humidity.Fractal(x * 0.0011f + 400.0f, z * 0.0011f, 3) * 1.4f;

        Biome biome;
        if (h < Chunk.SeaLevel - 2) biome = Biome.Ocean;
        else if (h <= Chunk.SeaLevel + 2 && land < 0.98f && mountain < 6.5f) biome = Biome.Beach;
        else if (mountain > 32.0f) biome = Biome.Mountains;
        else if (temperature > 0.2f && humidity < 0.08f) biome = Biome.Desert;
        else if (temperature < -0.22f) biome = Biome.Snowy;
        else if (humidity > 0.1f) biome = Biome.Forest;
        else biome = Biome.Plains;

        return new Column(h, biome, temperature, humidity, mountain);
    }

    /// <summary>The foliage color for a climate: lush in wet places, yellowed in dry heat, bluish in the cold.</summary>
    public static Vector3 FoliageColor(float temperature, float humidity)
    {
        var cold = new Vector3(0.42f, 0.62f, 0.47f);
        var temperate = new Vector3(0.55f, 0.75f, 0.36f);
        var dry = new Vector3(0.76f, 0.73f, 0.40f);
        var lush = new Vector3(0.38f, 0.66f, 0.26f);
        Vector3 c = Vector3.Lerp(cold, temperate, SmoothStep(-0.42f, -0.05f, temperature));
        c = Vector3.Lerp(c, dry, SmoothStep(0.05f, 0.5f, temperature) * (1.0f - SmoothStep(-0.1f, 0.3f, humidity)));
        c = Vector3.Lerp(c, lush, SmoothStep(0.0f, 0.45f, humidity) * 0.75f);
        return c;
    }

    public void Generate(Chunk chunk)
    {
        int ox = chunk.X * Chunk.Size;
        int oz = chunk.Z * Chunk.Size;

        // The columns of this chunk plus a one-block ring, for slopes.
        const int pad = Chunk.Size + 2;
        var columns = new Column[pad * pad];
        for (int z = 0; z < pad; z++)
        {
            for (int x = 0; x < pad; x++)
            {
                columns[z * pad + x] = ColumnAt(ox + x - 1, oz + z - 1);
            }
        }

        byte[] blocks = chunk.Blocks;
        var random = new Random(Hash(chunk.X, chunk.Z, 1));

        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                Column column = columns[(z + 1) * pad + x + 1];
                int h = column.Height;
                int wx = ox + x;
                int wz = oz + z;

                int slope = Math.Max(
                    Math.Abs(columns[(z + 1) * pad + x + 2].Height - columns[(z + 1) * pad + x].Height),
                    Math.Abs(columns[(z + 2) * pad + x + 1].Height - columns[z * pad + x + 1].Height));

                float surfaceNoise = _surface.Sample(wx * 0.08f, wz * 0.08f);
                int depth = 3 + (int)(surfaceNoise * 1.5f + 1.0f);
                int snowLine = 128 + (int)(surfaceNoise * 6.0f);

                BlockId top;
                BlockId filler;
                switch (column.Biome)
                {
                    case Biome.Ocean:
                        top = surfaceNoise > 0.25f ? BlockId.Gravel : h > Chunk.SeaLevel - 12 ? BlockId.Sand : BlockId.Dirt;
                        filler = top == BlockId.Gravel ? BlockId.Gravel : BlockId.Sand;
                        break;
                    case Biome.Beach:
                        top = column.Temperature < -0.3f ? BlockId.Gravel : BlockId.Sand;
                        filler = BlockId.Sand;
                        break;
                    case Biome.Desert:
                        top = BlockId.Sand;
                        filler = BlockId.Sand;
                        depth += 2;
                        break;
                    case Biome.Snowy:
                        top = BlockId.SnowyGrass;
                        filler = BlockId.Dirt;
                        break;
                    default:
                        top = BlockId.Grass;
                        filler = BlockId.Dirt;
                        break;
                }

                if (h >= snowLine)
                {
                    top = slope >= 3 ? BlockId.Stone : BlockId.Snow;
                    filler = slope >= 3 ? BlockId.Stone : BlockId.Snow;
                    depth = 1;
                }
                else if (slope >= 4 && column.Biome is Biome.Mountains or Biome.Plains or Biome.Forest or Biome.Snowy)
                {
                    top = surfaceNoise > 0.35f ? BlockId.Gravel : BlockId.Stone;
                    filler = BlockId.Stone;
                }
                else if (column.Biome == Biome.Mountains && h > 100)
                {
                    top = column.Temperature < -0.1f ? BlockId.SnowyGrass : BlockId.Grass;
                }

                for (int y = 0; y <= h; y++)
                {
                    BlockId id;
                    if (y == 0 || (y < 4 && random.Next(4) < 4 - y)) id = BlockId.Bedrock;
                    else if (y == h) id = top;
                    else if (y > h - depth) id = filler;
                    else if (filler == BlockId.Sand && y > h - depth - 4) id = BlockId.Sandstone;
                    else id = BlockId.Stone;
                    blocks[Chunk.Index(x, y, z)] = (byte)id;
                }

                for (int y = h + 1; y <= Chunk.SeaLevel; y++)
                {
                    blocks[Chunk.Index(x, y, z)] = (byte)BlockId.Water;
                }

                Vector3 foliage = FoliageColor(column.Temperature, column.Humidity);
                chunk.Foliage[z * Chunk.Size + x] = Pack(foliage);
            }
        }

        CarveCaves(chunk, columns);
        PlaceOres(chunk, random);
        PlaceTrees(chunk);
        PlacePlants(chunk, columns);
        chunk.UpdateHeightMap();
    }

    // Tunnels where two noise fields both cross zero (long winding "spaghetti"), and caverns where a third is high.
    // The noise is sampled every 4 blocks and interpolated, a fraction of the cost of sampling every block.
    private void CarveCaves(Chunk chunk, Column[] columns)
    {
        const int step = 4;
        const int nx = Chunk.Size / step + 1;
        int maxY = 0;
        for (int i = 0; i < columns.Length; i++) maxY = Math.Max(maxY, columns[i].Height);
        int ny = maxY / step + 2;

        var a = new float[nx * nx * ny];
        var b = new float[nx * nx * ny];
        var cavern = new float[nx * nx * ny];
        int ox = chunk.X * Chunk.Size;
        int oz = chunk.Z * Chunk.Size;
        for (int iy = 0; iy < ny; iy++)
        {
            for (int iz = 0; iz < nx; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    float wx = ox + ix * step;
                    float wy = iy * step;
                    float wz = oz + iz * step;
                    int i = (iy * nx + iz) * nx + ix;
                    a[i] = _caveA.Sample(wx * 0.016f, wy * 0.024f, wz * 0.016f);
                    b[i] = _caveB.Sample(wx * 0.016f, wy * 0.024f, wz * 0.016f);
                    cavern[i] = _cavern.Sample(wx * 0.011f, wy * 0.02f, wz * 0.011f);
                }
            }
        }

        const int pad = Chunk.Size + 2;
        byte[] blocks = chunk.Blocks;
        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                Column column = columns[(z + 1) * pad + x + 1];
                bool wet = column.Height <= Chunk.SeaLevel + 1;
                int ceiling = wet ? column.Height - 6 : column.Height;
                for (int y = 1; y <= ceiling; y++)
                {
                    float fx = (float)x / step;
                    float fy = (float)y / step;
                    float fz = (float)z / step;
                    float va = Trilinear(a, nx, fx, fy, fz);
                    float vb = Trilinear(b, nx, fx, fy, fz);

                    // Tunnels widen a little with depth; caverns only open up well below the surface.
                    float width = 0.0075f + (y < 40 ? 0.004f : 0.0f);
                    bool tunnel = va * va + vb * vb < width;
                    bool open = tunnel;
                    if (!open && y < column.Height - 12 && y < 56)
                    {
                        open = Trilinear(cavern, nx, fx, fy, fz) > 0.4f + (y < 12 ? (12 - y) * 0.03f : 0.0f);
                    }

                    if (!open) continue;

                    int index = Chunk.Index(x, y, z);
                    BlockId id = (BlockId)blocks[index];
                    if (id is BlockId.Bedrock or BlockId.Water) continue;
                    blocks[index] = 0;

                    // A tunnel that reaches the grass keeps the soil looking natural: no floating dirt.
                    if (y == column.Height - 1 && y + 1 < Chunk.Height && blocks[Chunk.Index(x, y + 1, z)] != 0)
                    {
                        blocks[Chunk.Index(x, y + 1, z)] = 0;
                    }
                }
            }
        }
    }

    private static float Trilinear(float[] grid, int n, float x, float y, float z)
    {
        int x0 = (int)x;
        int y0 = (int)y;
        int z0 = (int)z;
        int x1 = Math.Min(x0 + 1, n - 1);
        int z1 = Math.Min(z0 + 1, n - 1);
        int y1 = y0 + 1;
        float tx = x - x0;
        float ty = y - y0;
        float tz = z - z0;
        float Get(int ix, int iy, int iz) => grid[(iy * n + iz) * n + ix];
        float c00 = Lerp(Get(x0, y0, z0), Get(x1, y0, z0), tx);
        float c10 = Lerp(Get(x0, y0, z1), Get(x1, y0, z1), tx);
        float c01 = Lerp(Get(x0, y1, z0), Get(x1, y1, z0), tx);
        float c11 = Lerp(Get(x0, y1, z1), Get(x1, y1, z1), tx);
        return Lerp(Lerp(c00, c10, tz), Lerp(c01, c11, tz), ty);
    }

    private static void PlaceOres(Chunk chunk, Random random)
    {
        Vein(BlockId.CoalOre, 18, 5, 128, 9);
        Vein(BlockId.IronOre, 10, 5, 64, 6);
        Vein(BlockId.GoldOre, 3, 5, 32, 5);
        Vein(BlockId.DiamondOre, 1, 5, 16, 4);

        void Vein(BlockId ore, int count, int minY, int maxY, int size)
        {
            for (int i = 0; i < count; i++)
            {
                int x = random.Next(Chunk.Size);
                int y = random.Next(minY, maxY);
                int z = random.Next(Chunk.Size);
                int length = random.Next(size / 2, size + 1);
                for (int j = 0; j < length; j++)
                {
                    if (x is >= 0 and < Chunk.Size && z is >= 0 and < Chunk.Size && y is > 0 and < Chunk.Height)
                    {
                        int index = Chunk.Index(x, y, z);
                        if (chunk.Blocks[index] == (byte)BlockId.Stone) chunk.Blocks[index] = (byte)ore;
                    }

                    switch (random.Next(6))
                    {
                        case 0: x++; break;
                        case 1: x--; break;
                        case 2: y++; break;
                        case 3: y--; break;
                        case 4: z++; break;
                        default: z--; break;
                    }
                }
            }
        }
    }

    // Each chunk decides its trees from its own seed; a chunk draws the parts of its neighbours' trees that reach
    // into it, so canopies cross borders seamlessly without the chunks waiting for each other.
    private void PlaceTrees(Chunk chunk)
    {
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = chunk.X + dx;
                int cz = chunk.Z + dz;
                var random = new Random(Hash(cx, cz, 2));
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    int lx = random.Next(Chunk.Size);
                    int lz = random.Next(Chunk.Size);
                    float roll = (float)random.NextDouble();
                    int variant = random.Next(1 << 20);
                    int wx = cx * Chunk.Size + lx;
                    int wz = cz * Chunk.Size + lz;

                    // Only columns that would get grass (or sand, for cacti) can grow anything.
                    Column column = ColumnAt(wx, wz);
                    if (column.Height <= Chunk.SeaLevel || column.Height >= 126) continue;

                    float chance = column.Biome switch
                    {
                        Biome.Forest => 0.75f,
                        Biome.Plains => 0.07f,
                        Biome.Snowy => 0.38f,
                        Biome.Desert => 0.12f,
                        Biome.Mountains => 0.1f,
                        _ => 0.0f,
                    };
                    if (roll >= chance) continue;

                    // Steep ground keeps nothing.
                    int slope = Math.Max(Math.Abs(ColumnAt(wx + 1, wz).Height - ColumnAt(wx - 1, wz).Height),
                        Math.Abs(ColumnAt(wx, wz + 1).Height - ColumnAt(wx, wz - 1).Height));
                    if (slope >= 4) continue;

                    int baseX = wx - chunk.X * Chunk.Size;
                    int baseZ = wz - chunk.Z * Chunk.Size;
                    int y = column.Height + 1;
                    var tree = new Random(variant);
                    switch (column.Biome)
                    {
                        case Biome.Desert:
                            Cactus(chunk, baseX, y, baseZ, tree);
                            break;
                        case Biome.Snowy:
                        case Biome.Mountains:
                            Spruce(chunk, baseX, y, baseZ, tree);
                            break;
                        case Biome.Forest when tree.Next(3) == 0:
                            Oak(chunk, baseX, y, baseZ, tree, BlockId.BirchLog, BlockId.BirchLeaves, 5);
                            break;
                        default:
                            Oak(chunk, baseX, y, baseZ, tree, BlockId.OakLog, BlockId.OakLeaves, 4);
                            break;
                    }
                }
            }
        }
    }

    private static void Oak(Chunk chunk, int x, int y, int z, Random random, BlockId log, BlockId leaves, int minHeight)
    {
        int height = minHeight + random.Next(3);
        int top = y + height;
        for (int ly = top - 3; ly <= top; ly++)
        {
            int radius = ly >= top - 1 ? 1 : 2;
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    bool corner = Math.Abs(dx) == radius && Math.Abs(dz) == radius;
                    if (corner && (ly == top || random.Next(2) == 0)) continue;
                    Leaf(chunk, x + dx, ly, z + dz, leaves);
                }
            }
        }

        for (int ly = y; ly < top; ly++) Put(chunk, x, ly, z, log, replaceLeaves: true);
        Soil(chunk, x, y - 1, z);
    }

    private static void Spruce(Chunk chunk, int x, int y, int z, Random random)
    {
        int height = 7 + random.Next(4);
        int top = y + height;
        int radius = 0;
        for (int ly = top; ly >= y + 2; ly--)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (radius > 0 && Math.Abs(dx) == radius && Math.Abs(dz) == radius) continue;
                    Leaf(chunk, x + dx, ly, z + dz, BlockId.SpruceLeaves);
                }
            }

            // Tiers widen toward the bottom and step back in a little after each.
            radius = radius >= 1 + (top - ly) / 3 ? (radius > 1 ? 1 : 0) : radius + 1;
            radius = Math.Min(radius, 3);
        }

        Leaf(chunk, x, top + 1, z, BlockId.SpruceLeaves);
        for (int ly = y; ly < top; ly++) Put(chunk, x, ly, z, BlockId.SpruceLog, replaceLeaves: true);
        Soil(chunk, x, y - 1, z);
    }

    private static void Cactus(Chunk chunk, int x, int y, int z, Random random)
    {
        int height = 1 + random.Next(3);
        for (int ly = y; ly < y + height; ly++) Put(chunk, x, ly, z, BlockId.Cactus, replaceLeaves: false);
    }

    private static void Leaf(Chunk chunk, int x, int y, int z, BlockId leaves)
    {
        if (!Inside(x, y, z)) return;
        int index = Chunk.Index(x, y, z);
        BlockId current = (BlockId)chunk.Blocks[index];
        if (current == BlockId.Air || Blocks.Shape(current) == BlockShape.Plant) chunk.Blocks[index] = (byte)leaves;
    }

    private static void Put(Chunk chunk, int x, int y, int z, BlockId id, bool replaceLeaves)
    {
        if (!Inside(x, y, z)) return;
        int index = Chunk.Index(x, y, z);
        BlockId current = (BlockId)chunk.Blocks[index];
        if (current == BlockId.Air || Blocks.Shape(current) == BlockShape.Plant || (replaceLeaves && Blocks.Shape(current) == BlockShape.Cutout))
        {
            chunk.Blocks[index] = (byte)id;
        }
    }

    private static void Soil(Chunk chunk, int x, int y, int z)
    {
        if (!Inside(x, y, z)) return;
        int index = Chunk.Index(x, y, z);
        if (chunk.Blocks[index] is (byte)BlockId.Grass or (byte)BlockId.SnowyGrass) chunk.Blocks[index] = (byte)BlockId.Dirt;
    }

    private static bool Inside(int x, int y, int z) =>
        x is >= 0 and < Chunk.Size && z is >= 0 and < Chunk.Size && y is > 0 and < Chunk.Height;

    private void PlacePlants(Chunk chunk, Column[] columns)
    {
        const int pad = Chunk.Size + 2;
        var random = new Random(Hash(chunk.X, chunk.Z, 3));
        for (int z = 0; z < Chunk.Size; z++)
        {
            for (int x = 0; x < Chunk.Size; x++)
            {
                Column column = columns[(z + 1) * pad + x + 1];
                int y = column.Height;
                double roll = random.NextDouble();
                if (y + 1 >= Chunk.Height || chunk.Get(x, y + 1, z) != BlockId.Air) continue;

                BlockId ground = chunk.Get(x, y, z);
                BlockId plant = BlockId.Air;
                if (ground == BlockId.Grass)
                {
                    (double grass, double flowers) = column.Biome switch
                    {
                        Biome.Plains => (0.24, 0.03),
                        Biome.Forest => (0.18, 0.02),
                        Biome.Mountains => (0.08, 0.005),
                        _ => (0.1, 0.01),
                    };
                    if (roll < flowers) plant = random.Next(3) == 0 ? BlockId.Poppy : BlockId.Dandelion;
                    else if (roll < flowers + grass) plant = BlockId.TallGrass;
                }
                else if (ground == BlockId.SnowyGrass && roll < 0.03)
                {
                    plant = BlockId.TallGrass;
                }
                else if (ground == BlockId.Sand && column.Biome == Biome.Desert && roll < 0.012)
                {
                    plant = BlockId.DeadBush;
                }

                if (plant != BlockId.Air) chunk.Set(x, y + 1, z, plant);
            }
        }
    }

    private int Hash(int x, int z, int salt)
    {
        unchecked
        {
            int h = _seed * 73856093 ^ x * 19349663 ^ z * 83492791 ^ salt * 2654435;
            h ^= h >> 13;
            h *= 1274126177;
            return h ^ (h >> 16);
        }
    }

    public static int Pack(Vector3 c) =>
        ((int)(Math.Clamp(c.X, 0.0f, 1.0f) * 255.0f) << 16) | ((int)(Math.Clamp(c.Y, 0.0f, 1.0f) * 255.0f) << 8) | (int)(Math.Clamp(c.Z, 0.0f, 1.0f) * 255.0f);

    public static Vector3 Unpack(int c) => new(((c >> 16) & 255) / 255.0f, ((c >> 8) & 255) / 255.0f, (c & 255) / 255.0f);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }
}
