using System.Numerics;

namespace Voxelcraft;

/// <summary>Where a chunk is in its life: generated on a worker, meshed on a worker, uploaded on the main thread.</summary>
public enum ChunkState
{
    Generating,
    Generated,
    Ready,
}

/// <summary>
/// A 16×16 column of the world, <see cref="Height"/> blocks tall: one byte per block, the height of the highest
/// sky-blocking block per column (the cheap sky light), and the biome's foliage color per column. Its GPU meshes
/// are owned by the <see cref="WorldRenderer"/> once uploaded.
/// </summary>
public sealed class Chunk
{
    public const int Size = 16;
    public const int Height = 192;
    public const int SeaLevel = 62;

    public Chunk(int x, int z)
    {
        X = x;
        Z = z;
    }

    public int X { get; }

    public int Z { get; }

    public Vector3 Origin => new(X * Size, 0.0f, Z * Size);

    public readonly byte[] Blocks = new byte[Size * Size * Height];

    /// <summary>The y of the highest block in each column that dims sky light, or -1.</summary>
    public readonly short[] HeightMap = new short[Size * Size];

    /// <summary>The foliage color of each column, packed 0xRRGGBB.</summary>
    public readonly int[] Foliage = new int[Size * Size];

    /// <summary>Gets the highest y that holds anything but air — the mesher's upper bound.</summary>
    public int Top { get; set; }

    public volatile ChunkState State = ChunkState.Generating;

    /// <summary>Bumped by every edit, so a mesh built from older data is thrown away when it arrives.</summary>
    public int Version;

    /// <summary>Gets whether a mesh job for this chunk is in flight.</summary>
    public bool Meshing;

    /// <summary>Gets whether the chunk needs (re)meshing once its neighbours are ready.</summary>
    public bool Dirty = true;

    /// <summary>The number of glowing blocks in it; the mesher skips block light when no chunk around has any.</summary>
    public int EmissiveCount;

    /// <summary>Gets whether the player changed it (it is kept when unloaded).</summary>
    public bool Modified;

    public ChunkMesh? Mesh;

    public static int Index(int x, int y, int z) => (y * Size + z) * Size + x;

    public BlockId Get(int x, int y, int z) => (BlockId)Blocks[Index(x, y, z)];

    public void Set(int x, int y, int z, BlockId id) => Blocks[Index(x, y, z)] = (byte)id;

    /// <summary>Recomputes one column's height-map entry.</summary>
    public void UpdateColumn(int x, int z)
    {
        short top = -1;
        for (int y = Height - 1; y >= 0; y--)
        {
            if (Voxelcraft.Blocks.BlocksSky((BlockId)Blocks[Index(x, y, z)]))
            {
                top = (short)y;
                break;
            }
        }

        HeightMap[z * Size + x] = top;
    }

    /// <summary>Recomputes the height map and <see cref="Top"/> after generation.</summary>
    public void UpdateHeightMap()
    {
        int highest = 0;
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                UpdateColumn(x, z);
                for (int y = Height - 1; y > highest; y--)
                {
                    if (Blocks[Index(x, y, z)] != 0)
                    {
                        highest = y;
                        break;
                    }
                }
            }
        }

        Top = highest;
    }
}
