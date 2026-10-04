using System.Collections.Concurrent;

namespace Voxelcraft;

/// <summary>A growable float buffer, pooled across mesh jobs so meshing allocates almost nothing.</summary>
public sealed class FloatList
{
    private static readonly ConcurrentBag<FloatList> Pool = new();

    public float[] Data = new float[1 << 16];

    public int Count;

    public static FloatList Rent()
    {
        if (!Pool.TryTake(out FloatList? list)) list = new FloatList();
        list.Count = 0;
        return list;
    }

    public static void Return(FloatList? list)
    {
        if (list is not null && Pool.Count < 64) Pool.Add(list);
    }

    public ReadOnlySpan<float> Span => Data.AsSpan(0, Count);

    public void Add(float x, float y, float z, float u, float v, float a, float b, float c)
    {
        if (Count + 8 > Data.Length) Array.Resize(ref Data, Data.Length * 2);
        float[] d = Data;
        int i = Count;
        d[i] = x;
        d[i + 1] = y;
        d[i + 2] = z;
        d[i + 3] = u;
        d[i + 4] = v;
        d[i + 5] = a;
        d[i + 6] = b;
        d[i + 7] = c;
        Count = i + 8;
    }
}

/// <summary>The CPU result of meshing a chunk, waiting to be uploaded on the main thread.</summary>
public sealed class ChunkMeshData
{
    public required Chunk Chunk { get; init; }

    public required int Version { get; init; }

    public required FloatList Opaque { get; init; }

    public required FloatList Water { get; init; }

    public int MinY { get; set; }

    public int MaxY { get; set; }

    public void Release()
    {
        FloatList.Return(Opaque);
        FloatList.Return(Water);
    }
}

/// <summary>
/// Turns a chunk and its eight neighbours into vertices: only the faces that touch something see-through, each
/// with per-corner ambient occlusion and smooth light. Light is the Minecraft kind: sky light poured down every
/// column and spread sideways under overhangs and into caves, and block light flooding out of glowing blocks, both
/// 0..15 and baked into the vertices. Pure CPU work on immutable-enough data, so it runs on worker threads.
/// </summary>
/// <remarks>
/// A vertex is eight floats: the position in the chunk, the face UV, then <c>tile + 256·face</c>,
/// <c>ao + 4·sky + 64·blockLight + 1024·flags</c> and the foliage tint packed as 0xRRGGBB — all integers small
/// enough to be exact in a float, decoded by the shaders.
/// </remarks>
public static class ChunkMesher
{
    public const int FloatsPerVertex = 8;
    public const int FlagSway = 1;
    public const int FlagEmissive = 2;

    // Mesher-only: the face is emitted a second time facing the other way. Not stored in the vertex.
    private const int FlagTwoSided = 4;

    private const int P = Chunk.Size + 2;          // the chunk plus a one-block border
    private const int Reach = 15;                   // how far block light can travel
    private const int L = Chunk.Size + Reach * 2;   // the block-light region: the chunk plus its reach

    // Per face: the corner the quad starts at and its two edges, such that du × dv is the outward normal.
    private static readonly int[] OX = { 1, 0, 0, 0, 0, 1 };
    private static readonly int[] OY = { 0, 0, 1, 0, 0, 0 };
    private static readonly int[] OZ = { 1, 0, 1, 0, 1, 0 };
    private static readonly int[] UX = { 0, 0, 1, 1, 1, -1 };
    private static readonly int[] UY = { 0, 0, 0, 0, 0, 0 };
    private static readonly int[] UZ = { -1, 1, 0, 0, 0, 0 };
    private static readonly int[] VX = { 0, 0, 0, 0, 0, 0 };
    private static readonly int[] VY = { 1, 1, 0, 0, 1, 1 };
    private static readonly int[] VZ = { 0, 0, -1, 1, 0, 0 };

    private static readonly int[] CornerU = { 0, 1, 1, 0 };
    private static readonly int[] CornerV = { 0, 0, 1, 1 };

    /// <summary>Meshes the center of a 3×3 block of chunks (index <c>(dz+1)·3 + dx+1</c>; all nine present).</summary>
    public static ChunkMeshData Build(Chunk[] around, int version)
    {
        Chunk center = around[4];
        int top = 0;
        foreach (Chunk c in around) top = Math.Max(top, c.Top);
        int height = Math.Min(Chunk.Height, top + 2);

        // The padded block volume.
        var blocks = new byte[P * P * height];
        for (int pz = 0; pz < P; pz++)
        {
            for (int px = 0; px < P; px++)
            {
                int wx = px - 1;
                int wz = pz - 1;
                Chunk source = around[(Section(wz) + 1) * 3 + Section(wx) + 1];
                int lx = (wx + Chunk.Size) % Chunk.Size;
                int lz = (wz + Chunk.Size) % Chunk.Size;
                for (int y = 0; y < height; y++)
                {
                    blocks[(y * P + pz) * P + px] = source.Blocks[Chunk.Index(lx, y, lz)];
                }
            }
        }

        byte[] sky = SkyLight(blocks, height);
        byte[]? glow = BlockLight(around, height);

        var data = new ChunkMeshData
        {
            Chunk = center,
            Version = version,
            Opaque = FloatList.Rent(),
            Water = FloatList.Rent(),
            MinY = height,
            MaxY = 0,
        };

        var context = new Context(blocks, sky, glow, height, center);
        ulong[] mask = t_mask ??= new ulong[6 * MaskStride];
        int maxY = Math.Min(center.Top, height - 1);
        for (int y = 0; y <= maxY; y++)
        {
            for (int z = 0; z < Chunk.Size; z++)
            {
                for (int x = 0; x < Chunk.Size; x++)
                {
                    var id = (BlockId)blocks[(y * P + z + 1) * P + x + 1];
                    if (id == BlockId.Air) continue;

                    BlockShape shape = Blocks.Shape(id);
                    bool any = shape switch
                    {
                        BlockShape.Plant => Plant(context, data.Opaque, id, x, y, z),
                        BlockShape.Liquid => Liquid(context, data, mask, x, y, z),
                        _ => Cube(context, data, mask, id, x, y, z),
                    };

                    if (any)
                    {
                        data.MinY = Math.Min(data.MinY, y);
                        data.MaxY = Math.Max(data.MaxY, y + 1);
                    }
                }
            }
        }

        Merge(mask, data, maxY + 1);

        if (data.MaxY < data.MinY)
        {
            data.MinY = 0;
            data.MaxY = 0;
        }

        return data;
    }

    private static int Section(int local) => local < 0 ? -1 : local >= Chunk.Size ? 1 : 0;

    private readonly record struct Context(byte[] Blocks, byte[] Sky, byte[]? Glow, int Height, Chunk Chunk)
    {
        // Padded coordinates (the chunk's own blocks are 1..16); above the volume is open sky.
        public BlockId Block(int px, int y, int pz) =>
            y >= Height ? BlockId.Air : y < 0 ? BlockId.Bedrock : (BlockId)Blocks[(y * P + pz) * P + px];

        public int SkyAt(int px, int y, int pz) => y >= Height ? 15 : y < 0 ? 0 : Sky[(y * P + pz) * P + px];

        public int GlowAt(int px, int y, int pz)
        {
            if (Glow is null || y < 0 || y >= Height) return 0;
            return Glow[(y * L + pz - 1 + Reach) * L + px - 1 + Reach];
        }

        public bool Occludes(int px, int y, int pz)
        {
            BlockShape s = Voxelcraft.Blocks.Shape(Block(px, y, pz));
            return s is BlockShape.Opaque or BlockShape.Cutout;
        }

        public int Foliage(int x, int z) => Chunk.Foliage[z * Chunk.Size + x];
    }

    private static bool Cube(in Context c, ChunkMeshData data, ulong[] mask, BlockId id, int x, int y, int z)
    {
        BlockInfo info = Blocks.Get(id);
        int px = x + 1;
        int pz = z + 1;
        int tint = info.Tinted ? c.Foliage(x, z) : 0xFFFFFF;
        int flags = info.Emissive ? FlagEmissive : 0;
        bool any = false;

        for (int face = 0; face < 6; face++)
        {
            BlockId neighbour = c.Block(px + Faces.DX[face], y + Faces.DY[face], pz + Faces.DZ[face]);
            BlockShape ns = Blocks.Shape(neighbour);
            if (ns == BlockShape.Opaque) continue;
            if (ns == BlockShape.Cutout && (neighbour == id || (info.Leafy && Blocks.Get(neighbour).Leafy))) continue;

            Face(c, data, mask, face, info.Tile(face), tint, flags | (info.Leafy ? FlagTwoSided : 0), water: false, lowered: false, x, y, z);
            any = true;
        }

        return any;
    }

    private static bool Liquid(in Context c, ChunkMeshData data, ulong[] mask, int x, int y, int z)
    {
        int px = x + 1;
        int pz = z + 1;
        bool covered = c.Block(px, y + 1, pz) == BlockId.Water;
        bool any = false;
        for (int face = 0; face < 6; face++)
        {
            BlockId neighbour = c.Block(px + Faces.DX[face], y + Faces.DY[face], pz + Faces.DZ[face]);
            if (neighbour == BlockId.Water || Blocks.IsOpaque(neighbour)) continue;
            if (face == Faces.Up && covered) continue;
            Face(c, data, mask, face, Tiles.Water, 0xFFFFFF, 0, water: true, lowered: !covered, x, y, z);
            any = true;
        }

        return any;
    }

    // Lights the four corners of a block face. A face lit the same at every corner — most of them, out in the open
    // or deep in the dark — goes into the mask to be merged with its like; any other is emitted on its own.
    private static void Face(in Context c, ChunkMeshData data, ulong[] mask, int face, int tile, int tint, int flags,
        bool water, bool lowered, int x, int y, int z)
    {
        int fx = x + 1 + Faces.DX[face];
        int fy = y + Faces.DY[face];
        int fz = z + 1 + Faces.DZ[face];
        int frontSky = c.SkyAt(fx, fy, fz);
        int frontGlow = c.GlowAt(fx, fy, fz);
        bool emissive = (flags & FlagEmissive) != 0;
        bool twoSided = (flags & FlagTwoSided) != 0;
        flags &= 3;

        Span<int> ao = stackalloc int[4];
        Span<int> skies = stackalloc int[4];
        Span<int> glows = stackalloc int[4];
        for (int k = 0; k < 4; k++)
        {
            int su = CornerU[k] * 2 - 1;
            int sv = CornerV[k] * 2 - 1;
            int ax = fx + UX[face] * su;
            int ay = fy + UY[face] * su;
            int az = fz + UZ[face] * su;
            int bx = fx + VX[face] * sv;
            int by = fy + VY[face] * sv;
            int bz = fz + VZ[face] * sv;
            int cx = ax + VX[face] * sv;
            int cy = ay + VY[face] * sv;
            int cz = az + VZ[face] * sv;
            bool s1 = c.Occludes(ax, ay, az);
            bool s2 = c.Occludes(bx, by, bz);
            bool corner = c.Occludes(cx, cy, cz);
            ao[k] = emissive ? 3 : s1 && s2 ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (corner ? 1 : 0));

            // Smooth light: the average over the open cells around the corner.
            int skySum = frontSky;
            int glowSum = frontGlow;
            int n = 1;
            if (!s1) { skySum += c.SkyAt(ax, ay, az); glowSum += c.GlowAt(ax, ay, az); n++; }
            if (!s2) { skySum += c.SkyAt(bx, by, bz); glowSum += c.GlowAt(bx, by, bz); n++; }
            if (!corner && !(s1 && s2)) { skySum += c.SkyAt(cx, cy, cz); glowSum += c.GlowAt(cx, cy, cz); n++; }
            skies[k] = (skySum + n / 2) / n;
            glows[k] = emissive ? 15 : (glowSum + n / 2) / n;
        }

        bool uniform = ao[0] == ao[1] && ao[0] == ao[2] && ao[0] == ao[3]
                       && skies[0] == skies[1] && skies[0] == skies[2] && skies[0] == skies[3]
                       && glows[0] == glows[1] && glows[0] == glows[2] && glows[0] == glows[3];
        if (uniform)
        {
            mask[MaskIndex(face, x, y, z)] = Key(tile, tint, flags, ao[0], skies[0], glows[0], water, lowered, twoSided);
            return;
        }

        // Split the quad along the diagonal that keeps the occlusion gradient symmetric.
        FloatList list = water ? data.Water : data.Opaque;
        int start = ao[0] + ao[2] > ao[1] + ao[3] ? 1 : 0;
        float a = tile + 256 * face;
        for (int n = 0; n < (twoSided ? 8 : 4); n++)
        {
            // The second pass walks the corners backwards: the same quad, wound to face the other way.
            int i = n < 4 ? n : 7 - n;
            int k = (start + i) & 3;
            float u = CornerU[k];
            float v = CornerV[k];
            float vx = x + OX[face] + UX[face] * u + VX[face] * v;
            float vy = y + OY[face] + UY[face] * u + VY[face] * v;
            float vz = z + OZ[face] + UZ[face] * u + VZ[face] * v;
            if (lowered && vy > y + 0.5f) vy = y + WaterSurface;
            float b = ao[k] + 4 * skies[k] + 64 * glows[k] + 1024 * flags;
            list.Add(vx, vy, vz, u, v, a, b, tint);
        }
    }

    // ---- greedy merging ----

    private const int MaskStride = Chunk.Size * Chunk.Size * Chunk.Height;
    private const float WaterSurface = 0.875f;

    // Per face: whether the quad's U edge runs along +a or -a of the mask, and likewise its V edge along b.
    private static readonly int[] SA = { -1, 1, 1, 1, 1, -1 };
    private static readonly int[] SB = { 1, 1, -1, 1, 1, 1 };

    [ThreadStatic]
    private static ulong[]? t_mask;

    // The mask is laid out per face as [slice][a][b]: a along the face's U edge, b along its V edge.
    private static int MaskIndex(int face, int x, int y, int z) => face switch
    {
        0 or 1 => face * MaskStride + (x * Chunk.Size + z) * Chunk.Height + y,
        2 or 3 => face * MaskStride + (y * Chunk.Size + x) * Chunk.Size + z,
        _ => face * MaskStride + (z * Chunk.Size + x) * Chunk.Height + y,
    };

    private static ulong Key(int tile, int tint, int flags, int ao, int sky, int glow, bool water, bool lowered, bool twoSided = false) =>
        (uint)(tile & 255) | ((ulong)(uint)(tint & 0xFFFFFF) << 8) | ((ulong)(uint)(flags & 3) << 32) | ((ulong)(uint)(ao & 3) << 34)
        | ((ulong)(uint)(sky & 15) << 36) | ((ulong)(uint)(glow & 15) << 40) | (water ? 1UL << 44 : 0) | (lowered ? 1UL << 45 : 0)
        | (twoSided ? 1UL << 47 : 0) | (1UL << 46);

    // Merges equal neighbouring faces into rectangles — first along V, then along U — and emits them. Every cell it
    // reads is cleared, so the mask is empty again for the next chunk.
    private static void Merge(ulong[] mask, ChunkMeshData data, int height)
    {
        for (int face = 0; face < 6; face++)
        {
            bool horizontal = face is Faces.Up or Faces.Down;
            int slices = horizontal ? height : Chunk.Size;
            int bCount = horizontal ? Chunk.Size : height;
            int bStride = horizontal ? Chunk.Size : Chunk.Height;
            int baseIndex = face * MaskStride;
            for (int s = 0; s < slices; s++)
            {
                for (int a = 0; a < Chunk.Size; a++)
                {
                    int row = baseIndex + (s * Chunk.Size + a) * bStride;
                    for (int b = 0; b < bCount; b++)
                    {
                        ulong key = mask[row + b];
                        if (key == 0) continue;

                        // Water's lowered top edge can't stretch over several blocks of height.
                        bool lockV = !horizontal && (key & (1UL << 45)) != 0;
                        int h = 1;
                        while (!lockV && b + h < bCount && mask[row + b + h] == key) h++;

                        int w = 1;
                        while (a + w < Chunk.Size)
                        {
                            int next = baseIndex + (s * Chunk.Size + a + w) * bStride + b;
                            bool same = true;
                            for (int k = 0; k < h && same; k++) same = mask[next + k] == key;
                            if (!same) break;
                            w++;
                        }

                        for (int i = 0; i < w; i++)
                        {
                            Array.Clear(mask, baseIndex + (s * Chunk.Size + a + i) * bStride + b, h);
                        }

                        Emit(data, face, s, a, a + w - 1, b, b + h - 1, key);
                    }
                }
            }
        }
    }

    private static void Emit(ChunkMeshData data, int face, int slice, int a0, int a1, int b0, int b1, ulong key)
    {
        int tile = (int)(key & 255);
        float tint = (int)((key >> 8) & 0xFFFFFF);
        int flags = (int)((key >> 32) & 3);
        float light = (int)((key >> 34) & 3) + 4 * (int)((key >> 36) & 15) + 64 * (int)((key >> 40) & 15) + 1024 * flags;
        bool water = (key & (1UL << 44)) != 0;
        bool lowered = (key & (1UL << 45)) != 0;
        bool twoSided = (key & (1UL << 47)) != 0;
        FloatList list = water ? data.Water : data.Opaque;
        float tileFace = tile + 256 * face;
        float width = a1 - a0 + 1;
        float height = b1 - b0 + 1;

        for (int n = 0; n < (twoSided ? 8 : 4); n++)
        {
            int k = n < 4 ? n : 7 - n;
            int su = CornerU[k];
            int sv = CornerV[k];
            int a = (SA[face] > 0) == (su == 1) ? a1 : a0;
            int b = (SB[face] > 0) == (sv == 1) ? b1 : b0;
            int x;
            int y;
            int z;
            switch (face)
            {
                case 0:
                case 1:
                    x = slice; z = a; y = b;
                    break;
                case 2:
                case 3:
                    y = slice; x = a; z = b;
                    break;
                default:
                    z = slice; x = a; y = b;
                    break;
            }

            float vx = x + OX[face] + UX[face] * su + VX[face] * sv;
            float vy = y + OY[face] + UY[face] * su + VY[face] * sv;
            float vz = z + OZ[face] + UZ[face] * su + VZ[face] * sv;
            if (lowered && vy > y + 0.5f) vy = y + WaterSurface;
            list.Add(vx, vy, vz, su * width, sv * height, tileFace, light, tint);
        }
    }

    // Two crossed quads, each drawn from both sides, nudged off the grid a little so fields of grass look natural.
    private static bool Plant(in Context c, FloatList list, BlockId id, int x, int y, int z)
    {
        BlockInfo info = Blocks.Get(id);
        int px = x + 1;
        int pz = z + 1;
        int sky = c.SkyAt(px, y, pz);
        int glow = c.GlowAt(px, y, pz);
        float tint = info.Tinted ? c.Foliage(x, z) : 0xFFFFFF;
        float a = info.Side + 256 * Faces.Plant;

        uint h = (uint)((x + c.Chunk.X * Chunk.Size) * 73856093 ^ (z + c.Chunk.Z * Chunk.Size) * 19349663);
        h = (h ^ (h >> 13)) * 0x5bd1e995;
        float jx = ((h & 0xFF) / 255.0f - 0.5f) * 0.3f;
        float jz = (((h >> 8) & 0xFF) / 255.0f - 0.5f) * 0.3f;
        float scale = 0.85f + ((h >> 16) & 0xFF) / 255.0f * 0.25f;

        float cx = x + 0.5f + jx;
        float cz = z + 0.5f + jz;
        const float r = 0.45f;
        float height = MathF.Min(scale, 1.0f);
        for (int diagonal = 0; diagonal < 2; diagonal++)
        {
            const float dx = r;
            float dz = diagonal == 0 ? r : -r;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? 1.0f : -1.0f;
                float x0 = cx - dx * sx;
                float z0 = cz - dz * sx;
                float x1 = cx + dx * sx;
                float z1 = cz + dz * sx;
                float bottom = 3 + 4 * sky + 64 * glow;
                float topB = bottom + (info.Sways ? 1024 * FlagSway : 0);
                list.Add(x0, y, z0, 0, 0, a, bottom, tint);
                list.Add(x1, y, z1, 1, 0, a, bottom, tint);
                list.Add(x1, y + height, z1, 1, 1, a, topB, tint);
                list.Add(x0, y + height, z0, 0, 1, a, topB, tint);
            }
        }

        return true;
    }

    // Sky light: 15 poured down each column until something stops it (leaves and water dim it as it passes), then
    // spread breadth-first through open cells, one level per block, so overhangs and cave mouths fade into the dark.
    private static byte[] SkyLight(byte[] blocks, int height)
    {
        var light = new byte[blocks.Length];
        for (int pz = 0; pz < P; pz++)
        {
            for (int px = 0; px < P; px++)
            {
                int level = 15;
                for (int y = height - 1; y >= 0 && level > 0; y--)
                {
                    int i = (y * P + pz) * P + px;
                    var id = (BlockId)blocks[i];
                    BlockShape shape = Blocks.Shape(id);
                    if (shape == BlockShape.Opaque) level = 0;
                    else if (shape == BlockShape.Liquid || (shape == BlockShape.Cutout && id != BlockId.Glass)) level = Math.Max(0, level - 1);
                    light[i] = (byte)level;
                }
            }
        }

        var queue = new Queue<int>();
        for (int y = 0; y < height; y++)
        {
            for (int pz = 0; pz < P; pz++)
            {
                for (int px = 0; px < P; px++)
                {
                    int i = (y * P + pz) * P + px;
                    int level = light[i];
                    if (level < 2) continue;
                    // Only cells next to a darker open cell can spread anything.
                    if ((px > 0 && light[i - 1] < level - 1 && !Opaque(blocks[i - 1])) ||
                        (px < P - 1 && light[i + 1] < level - 1 && !Opaque(blocks[i + 1])) ||
                        (pz > 0 && light[i - P] < level - 1 && !Opaque(blocks[i - P])) ||
                        (pz < P - 1 && light[i + P] < level - 1 && !Opaque(blocks[i + P])) ||
                        (y > 0 && light[i - P * P] < level - 1 && !Opaque(blocks[i - P * P])))
                    {
                        queue.Enqueue(i);
                    }
                }
            }
        }

        Spread(queue, light, blocks, P, P, height);
        return light;
    }

    // Block light: flood-filled from every glowing block within reach of this chunk, so light crosses chunk borders
    // without seams. Skipped entirely (null) when no glowing block is near.
    private static byte[]? BlockLight(Chunk[] around, int height)
    {
        bool any = false;
        foreach (Chunk c in around) any |= c.EmissiveCount > 0;
        if (!any) return null;

        var blocks = new byte[L * L * height];
        var light = new byte[blocks.Length];
        var queue = new Queue<int>();
        for (int rz = 0; rz < L; rz++)
        {
            for (int rx = 0; rx < L; rx++)
            {
                int wx = rx - Reach;
                int wz = rz - Reach;
                Chunk source = around[(Section(wz) + 1) * 3 + Section(wx) + 1];
                int lx = (wx + Chunk.Size) % Chunk.Size;
                int lz = (wz + Chunk.Size) % Chunk.Size;
                for (int y = 0; y < height; y++)
                {
                    byte id = source.Blocks[Chunk.Index(lx, y, lz)];
                    int i = (y * L + rz) * L + rx;
                    blocks[i] = id;
                    if (Blocks.Get((BlockId)id).Emissive)
                    {
                        light[i] = 15;
                        queue.Enqueue(i);
                    }
                }
            }
        }

        Spread(queue, light, blocks, L, L, height);
        return light;
    }

    private static bool Opaque(byte id) => Blocks.IsOpaque((BlockId)id);

    private static void Spread(Queue<int> queue, byte[] light, byte[] blocks, int sizeX, int sizeZ, int height)
    {
        int layer = sizeX * sizeZ;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int level = light[i] - 1;
            if (level <= 0) continue;
            int x = i % sizeX;
            int z = i / sizeX % sizeZ;
            int y = i / layer;
            if (x > 0) Try(i - 1);
            if (x < sizeX - 1) Try(i + 1);
            if (z > 0) Try(i - sizeX);
            if (z < sizeZ - 1) Try(i + sizeX);
            if (y > 0) Try(i - layer);
            if (y < height - 1) Try(i + layer);

            void Try(int n)
            {
                var id = (BlockId)blocks[n];
                BlockShape shape = Blocks.Shape(id);
                if (shape == BlockShape.Opaque) return;
                int value = shape == BlockShape.Liquid || (shape == BlockShape.Cutout && id != BlockId.Glass) ? level - 1 : level;
                if (light[n] >= value) return;
                light[n] = (byte)value;
                queue.Enqueue(n);
            }
        }
    }
}
