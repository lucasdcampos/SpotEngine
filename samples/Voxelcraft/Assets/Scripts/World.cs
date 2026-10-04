using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using Spot.Framework;

namespace Voxelcraft;

/// <summary>A block position in the world.</summary>
public readonly record struct BlockPos(int X, int Y, int Z)
{
    public static BlockPos Floor(Vector3 p) => new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));

    public Vector3 Center => new(X + 0.5f, Y + 0.5f, Z + 0.5f);

    public static BlockPos operator +(BlockPos a, BlockPos b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

/// <summary>What the crosshair is on.</summary>
public readonly record struct BlockHit(BlockPos Position, BlockPos Normal, BlockId Block, float Distance);

/// <summary>
/// The infinite world: the loaded chunks around the player, streamed in and out as they move. Generation and
/// meshing run on the thread pool (in the order of distance, nearest first), and their results are uploaded on the
/// main thread within a time budget, so walking never hitches. Edits remesh the touched chunks immediately.
/// Chunks the player changed are kept when they unload, so a build is still there when you come back.
/// </summary>
public sealed class World : IDisposable
{
    private readonly Dictionary<long, Chunk> _chunks = new();
    private readonly Dictionary<long, Chunk> _kept = new();
    private readonly ConcurrentQueue<Chunk> _generated = new();
    private readonly ConcurrentQueue<ChunkMeshData> _meshed = new();
    private readonly QuadIndices _indices = new();
    private readonly List<Chunk> _unload = new();
    private readonly bool _threaded;
    private (int X, int Z)[] _offsets = Array.Empty<(int, int)>();
    private int _offsetsRadius = -1;
    private int _generating;
    private int _meshing;
    private volatile bool _disposed;

    public World(int seed, int renderDistance)
    {
        Generator = new TerrainGenerator(seed);
        RenderDistance = renderDistance;

        // The browser runs .NET on one thread: there, jobs run inline within the frame budget instead.
        _threaded = !OperatingSystem.IsBrowser() && Environment.ProcessorCount > 1;
        Workers = _threaded ? Math.Clamp(Environment.ProcessorCount - 1, 1, 8) : 1;
    }

    public TerrainGenerator Generator { get; }

    /// <summary>Gets or sets how many chunks are drawn in every direction.</summary>
    public int RenderDistance { get; set; }

    public int Workers { get; }

    public IReadOnlyCollection<Chunk> Chunks => _chunks.Values;

    public int PendingJobs => _generating + _meshing;

    /// <summary>Gets how long the last frame's uploads and scheduling took, in milliseconds.</summary>
    public float UpdateMilliseconds { get; private set; }

    public static long Key(int x, int z) => ((long)x << 32) | (uint)z;

    public static int ChunkCoord(int block) => block >> 4;

    public Chunk? GetChunk(int cx, int cz) => _chunks.TryGetValue(Key(cx, cz), out Chunk? chunk) ? chunk : null;

    public BlockId GetBlock(int x, int y, int z)
    {
        if (y < 0) return BlockId.Bedrock;
        if (y >= Chunk.Height) return BlockId.Air;
        Chunk? chunk = GetChunk(x >> 4, z >> 4);
        if (chunk is null || chunk.State == ChunkState.Generating) return BlockId.Air;
        return chunk.Get(x & 15, y, z & 15);
    }

    public BlockId GetBlock(BlockPos p) => GetBlock(p.X, p.Y, p.Z);

    /// <summary>Gets whether the chunk holding a block is generated (the player waits on unloaded ground).</summary>
    public bool IsLoaded(int x, int z) => GetChunk(x >> 4, z >> 4) is { State: not ChunkState.Generating };

    /// <summary>Gets whether every chunk within <paramref name="radius"/> chunks of a point is drawn.</summary>
    public bool IsReadyAround(Vector3 position, int radius)
    {
        int cx = ChunkCoord((int)MathF.Floor(position.X));
        int cz = ChunkCoord((int)MathF.Floor(position.Z));
        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (GetChunk(cx + dx, cz + dz) is not { State: ChunkState.Ready }) return false;
            }
        }

        return true;
    }

    /// <summary>Gets the share of the chunks within <paramref name="radius"/> that are drawn, 0..1.</summary>
    public float Progress(Vector3 position, int radius)
    {
        int cx = ChunkCoord((int)MathF.Floor(position.X));
        int cz = ChunkCoord((int)MathF.Floor(position.Z));
        int ready = 0;
        int total = 0;
        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                total++;
                if (GetChunk(cx + dx, cz + dz) is { State: ChunkState.Ready }) ready++;
            }
        }

        return total == 0 ? 1.0f : (float)ready / total;
    }

    /// <summary>Finds dry land near the origin to start on: the first grassy column on a spiral outwards.</summary>
    public Vector3 FindSpawn()
    {
        for (int ring = 0; ring < 200; ring++)
        {
            for (int i = 0; i < Math.Max(1, ring * 8); i++)
            {
                float angle = i / (float)Math.Max(1, ring * 8) * MathF.Tau;
                int x = (int)(MathF.Cos(angle) * ring * 24);
                int z = (int)(MathF.Sin(angle) * ring * 24);
                Column column = Generator.ColumnAt(x, z);
                if (column.Biome is Biome.Plains or Biome.Forest && column.Height > Chunk.SeaLevel + 2 && column.Height < 100)
                {
                    return new Vector3(x + 0.5f, column.Height + 1.0f, z + 0.5f);
                }
            }
        }

        return new Vector3(0.5f, Generator.ColumnAt(0, 0).Height + 1.0f, 0.5f);
    }

    /// <summary>The surface height at a column once its chunk is loaded: the top block you could stand on.</summary>
    public int SurfaceAt(int x, int z)
    {
        for (int y = Chunk.Height - 1; y > 0; y--)
        {
            if (Blocks.IsSolid(GetBlock(x, y, z)) || GetBlock(x, y, z) == BlockId.Water) return y;
        }

        return 0;
    }

    /// <summary>
    /// Streams the world around <paramref name="focus"/>: uploads finished meshes (within
    /// <paramref name="budgetMilliseconds"/>), schedules generation and meshing nearest first, and drops chunks
    /// that fell out of range.
    /// </summary>
    public void Update(Vector3 focus, float budgetMilliseconds)
    {
        long start = Stopwatch.GetTimestamp();
        int pcx = ChunkCoord((int)MathF.Floor(focus.X));
        int pcz = ChunkCoord((int)MathF.Floor(focus.Z));
        int radius = RenderDistance;
        EnsureOffsets(radius + 1);

        while (_generated.TryDequeue(out Chunk? chunk))
        {
            Interlocked.Decrement(ref _generating);
            chunk.State = ChunkState.Generated;
        }

        int uploads = 0;
        while (_meshed.TryPeek(out _))
        {
            if (uploads > 0 && Elapsed(start) > budgetMilliseconds) break;
            if (!_meshed.TryDequeue(out ChunkMeshData? data)) break;
            Interlocked.Decrement(ref _meshing);
            Apply(data);
            uploads++;
        }

        // Without worker threads the jobs run here, nearest first, until the budget is spent.
        Schedule(pcx, pcz, radius, start, budgetMilliseconds);
        Unload(pcx, pcz, radius + 3);
        UpdateMilliseconds = Elapsed(start);
    }

    private void Schedule(int pcx, int pcz, int radius, long start, float budget)
    {
        int maxJobs = _threaded ? Workers * 2 : int.MaxValue;
        foreach ((int dx, int dz) in _offsets)
        {
            if (!_threaded && Elapsed(start) > budget) return;
            int cx = pcx + dx;
            int cz = pcz + dz;
            long key = Key(cx, cz);
            if (!_chunks.TryGetValue(key, out Chunk? chunk))
            {
                if (_generating >= maxJobs) continue;
                if (_kept.Remove(key, out Chunk? kept))
                {
                    kept.Dirty = true;
                    kept.State = ChunkState.Generated;
                    _chunks[key] = kept;
                    continue;
                }

                chunk = new Chunk(cx, cz);
                _chunks[key] = chunk;
                Interlocked.Increment(ref _generating);
                if (_threaded) ThreadPool.QueueUserWorkItem(static state => state.World.GenerateJob(state.Chunk), (World: this, Chunk: chunk), false);
                else GenerateJob(chunk);
                continue;
            }

            // Mesh the chunks inside the render distance once all their neighbours exist.
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) > radius || chunk.State == ChunkState.Generating || !chunk.Dirty || chunk.Meshing)
            {
                continue;
            }

            if (_meshing >= maxJobs) continue;
            Chunk[]? around = Around(cx, cz);
            if (around is null) continue;

            chunk.Dirty = false;
            chunk.Meshing = true;
            Interlocked.Increment(ref _meshing);
            int version = chunk.Version;
            if (_threaded) ThreadPool.QueueUserWorkItem(static state => state.World.MeshJob(state.Around, state.Version), (World: this, Around: around, Version: version), false);
            else MeshJob(around, version);
        }
    }

    private Chunk[]? Around(int cx, int cz)
    {
        var around = new Chunk[9];
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Chunk? neighbour = GetChunk(cx + dx, cz + dz);
                if (neighbour is null || neighbour.State == ChunkState.Generating) return null;
                around[(dz + 1) * 3 + dx + 1] = neighbour;
            }
        }

        return around;
    }

    private void GenerateJob(Chunk chunk)
    {
        if (_disposed) return;
        try
        {
            Generator.Generate(chunk);
        }
        catch (Exception ex)
        {
            Log.Error("Voxelcraft: generating chunk ({0}, {1}) failed: {2}", chunk.X, chunk.Z, ex.Message);
        }

        _generated.Enqueue(chunk);
    }

    private void MeshJob(Chunk[] around, int version)
    {
        if (_disposed) return;
        try
        {
            _meshed.Enqueue(ChunkMesher.Build(around, version));
        }
        catch (Exception ex)
        {
            Log.Error("Voxelcraft: meshing chunk ({0}, {1}) failed: {2}", around[4].X, around[4].Z, ex.Message);
            around[4].Meshing = false;
            Interlocked.Decrement(ref _meshing);
        }
    }

    private void Apply(ChunkMeshData data)
    {
        Chunk chunk = data.Chunk;
        chunk.Meshing = false;
        try
        {
            // A chunk edited (or unloaded) since the job started gets a fresh job instead.
            if (data.Version != chunk.Version || !_chunks.TryGetValue(Key(chunk.X, chunk.Z), out Chunk? current) || current != chunk)
            {
                return;
            }

            chunk.Mesh?.Dispose();
            chunk.Mesh = ChunkMesh.Upload(data, _indices);
            chunk.State = ChunkState.Ready;
        }
        finally
        {
            data.Release();
        }
    }

    private void Unload(int pcx, int pcz, int keepRadius)
    {
        _unload.Clear();
        foreach (Chunk chunk in _chunks.Values)
        {
            if (Math.Max(Math.Abs(chunk.X - pcx), Math.Abs(chunk.Z - pcz)) > keepRadius && !chunk.Meshing && chunk.State != ChunkState.Generating)
            {
                _unload.Add(chunk);
            }
        }

        foreach (Chunk chunk in _unload)
        {
            long key = Key(chunk.X, chunk.Z);
            _chunks.Remove(key);
            chunk.Mesh?.Dispose();
            chunk.Mesh = null;
            if (chunk.Modified) _kept[key] = chunk;
        }
    }

    /// <summary>Places or removes a block, remeshing the chunks that show it right away.</summary>
    public bool SetBlock(BlockPos p, BlockId id)
    {
        if (p.Y <= 0 || p.Y >= Chunk.Height) return false;
        Chunk? chunk = GetChunk(p.X >> 4, p.Z >> 4);
        if (chunk is null || chunk.State == ChunkState.Generating) return false;

        int lx = p.X & 15;
        int lz = p.Z & 15;
        BlockId old = chunk.Get(lx, p.Y, lz);
        if (old == id) return false;

        chunk.Set(lx, p.Y, lz, id);
        chunk.Modified = true;
        chunk.Version++;
        chunk.Top = Math.Max(chunk.Top, p.Y);
        if (Blocks.Get(old).Emissive) chunk.EmissiveCount--;
        if (Blocks.Get(id).Emissive) chunk.EmissiveCount++;
        chunk.UpdateColumn(lx, lz);

        // Plants need ground: breaking the block under one breaks the plant too.
        BlockId above = GetBlock(p.X, p.Y + 1, p.Z);
        if (!Blocks.Supports(id) && Blocks.Shape(above) == BlockShape.Plant)
        {
            SetBlock(new BlockPos(p.X, p.Y + 1, p.Z), BlockId.Air);
        }

        // The edited chunk and any neighbour whose border shows this block are rebuilt now; when light from a
        // glowing block can reach further, the rest of the ring is rebuilt by the workers.
        bool glow = false;
        for (int dz = -1; dz <= 1 && !glow; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (GetChunk(chunk.X + dx, chunk.Z + dz) is { EmissiveCount: > 0 }) glow = true;
            }
        }

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                bool touches = (dx == 0 || (dx < 0 ? lx == 0 : lx == 15)) && (dz == 0 || (dz < 0 ? lz == 0 : lz == 15));
                Chunk? neighbour = GetChunk(chunk.X + dx, chunk.Z + dz);
                if (neighbour is null) continue;
                if (touches) Remesh(neighbour);
                else if (glow)
                {
                    neighbour.Version++;
                    neighbour.Dirty = true;
                }
            }
        }

        return true;
    }

    private void Remesh(Chunk chunk)
    {
        Chunk[]? around = Around(chunk.X, chunk.Z);
        if (around is null)
        {
            chunk.Dirty = true;
            return;
        }

        chunk.Version++;
        ChunkMeshData data = ChunkMesher.Build(around, chunk.Version);
        chunk.Mesh?.Dispose();
        chunk.Mesh = ChunkMesh.Upload(data, _indices);
        chunk.State = ChunkState.Ready;
        chunk.Dirty = false;
        data.Release();
    }

    /// <summary>
    /// Walks the grid along a ray (Amanatides–Woo) and returns the first selectable block, with the face it
    /// entered through.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out BlockHit hit)
    {
        hit = default;
        if (direction.LengthSquared() < 1e-8f) return false;
        direction = Vector3.Normalize(direction);

        var cell = BlockPos.Floor(origin);
        int stepX = Math.Sign(direction.X);
        int stepY = Math.Sign(direction.Y);
        int stepZ = Math.Sign(direction.Z);
        float tDeltaX = stepX != 0 ? MathF.Abs(1.0f / direction.X) : float.MaxValue;
        float tDeltaY = stepY != 0 ? MathF.Abs(1.0f / direction.Y) : float.MaxValue;
        float tDeltaZ = stepZ != 0 ? MathF.Abs(1.0f / direction.Z) : float.MaxValue;
        float tMaxX = stepX != 0 ? ((stepX > 0 ? cell.X + 1 - origin.X : origin.X - cell.X) * tDeltaX) : float.MaxValue;
        float tMaxY = stepY != 0 ? ((stepY > 0 ? cell.Y + 1 - origin.Y : origin.Y - cell.Y) * tDeltaY) : float.MaxValue;
        float tMaxZ = stepZ != 0 ? ((stepZ > 0 ? cell.Z + 1 - origin.Z : origin.Z - cell.Z) * tDeltaZ) : float.MaxValue;

        var normal = new BlockPos(0, 0, 0);
        float t = 0.0f;
        while (t <= maxDistance)
        {
            BlockId id = GetBlock(cell);
            if (Blocks.IsSelectable(id))
            {
                hit = new BlockHit(cell, normal, id, t);
                return true;
            }

            if (tMaxX < tMaxY && tMaxX < tMaxZ)
            {
                t = tMaxX;
                tMaxX += tDeltaX;
                cell = cell with { X = cell.X + stepX };
                normal = new BlockPos(-stepX, 0, 0);
            }
            else if (tMaxY < tMaxZ)
            {
                t = tMaxY;
                tMaxY += tDeltaY;
                cell = cell with { Y = cell.Y + stepY };
                normal = new BlockPos(0, -stepY, 0);
            }
            else
            {
                t = tMaxZ;
                tMaxZ += tDeltaZ;
                cell = cell with { Z = cell.Z + stepZ };
                normal = new BlockPos(0, 0, -stepZ);
            }
        }

        return false;
    }

    private void EnsureOffsets(int radius)
    {
        if (radius == _offsetsRadius) return;
        var offsets = new List<(int, int)>();
        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                offsets.Add((dx, dz));
            }
        }

        _offsets = offsets.OrderBy(o => o.Item1 * o.Item1 + o.Item2 * o.Item2).ToArray();
        _offsetsRadius = radius;
    }

    private static float Elapsed(long start) => (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    public void Dispose()
    {
        _disposed = true;
        foreach (Chunk chunk in _chunks.Values)
        {
            chunk.Mesh?.Dispose();
            chunk.Mesh = null;
        }

        _chunks.Clear();
        _kept.Clear();
        while (_meshed.TryDequeue(out ChunkMeshData? data)) data.Release();
        _indices.Dispose();
    }
}
