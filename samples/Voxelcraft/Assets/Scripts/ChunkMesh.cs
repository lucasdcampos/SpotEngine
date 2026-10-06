using Spot.Engine.Graphics;

namespace Voxelcraft;

/// <summary>
/// A chunk's geometry on the GPU: the opaque and cutout blocks in one buffer, the water in another. Every quad
/// shares one index pattern, so the chunks share one index buffer (<see cref="QuadIndices"/>).
/// </summary>
public sealed class ChunkMesh : IDisposable
{
    private static readonly ShaderDataType[] Layout =
    {
        ShaderDataType.Float3, // position in the chunk
        ShaderDataType.Float2, // face UV
        ShaderDataType.Float,  // tile + 256 * face
        ShaderDataType.Float,  // ao + 4 * sky + 64 * block light + 1024 * flags
        ShaderDataType.Float,  // foliage tint, 0xRRGGBB
    };

    private VertexArray? _opaque;
    private VertexBuffer? _opaqueVertices;
    private VertexArray? _water;
    private VertexBuffer? _waterVertices;

    public uint OpaqueIndexCount { get; private set; }

    public uint WaterIndexCount { get; private set; }

    public VertexArray? Opaque => _opaque;

    public VertexArray? Water => _water;

    public float MinY { get; private set; }

    public float MaxY { get; private set; }

    public int Quads { get; private set; }

    public static ChunkMesh Upload(ChunkMeshData data, QuadIndices indices)
    {
        var mesh = new ChunkMesh { MinY = data.MinY, MaxY = data.MaxY };
        mesh.Quads = (data.Opaque.Count + data.Water.Count) / (ChunkMesher.FloatsPerVertex * 4);
        (mesh._opaque, mesh._opaqueVertices, mesh.OpaqueIndexCount) = Create(data.Opaque, indices);
        (mesh._water, mesh._waterVertices, mesh.WaterIndexCount) = Create(data.Water, indices);
        return mesh;
    }

    private static (VertexArray?, VertexBuffer?, uint) Create(FloatList vertices, QuadIndices indices)
    {
        int quads = vertices.Count / (ChunkMesher.FloatsPerVertex * 4);
        if (quads == 0) return (null, null, 0);

        // The vertex array is bound first, so creating or growing the shared index buffer can only ever bind it
        // into this array — never into one the engine happens to have bound.
        var array = new VertexArray();
        var buffer = new VertexBuffer(vertices.Span, Layout);
        array.AddVertexBuffer(buffer);
        array.SetIndexBuffer(indices.Ensure(quads));
        return (array, buffer, (uint)quads * 6);
    }

    public void Dispose()
    {
        _opaque?.Dispose();
        _opaqueVertices?.Dispose();
        _water?.Dispose();
        _waterVertices?.Dispose();
        _opaque = null;
        _water = null;
        _opaqueVertices = null;
        _waterVertices = null;
    }
}

/// <summary>The index buffer every chunk shares: quads as two triangles, (0 1 2) (2 3 0), grown on demand.</summary>
public sealed class QuadIndices : IDisposable
{
    private readonly List<IndexBuffer> _buffers = new();
    private IndexBuffer? _current;
    private int _capacity;

    /// <summary>Returns an index buffer with room for at least <paramref name="quads"/> quads.</summary>
    public IndexBuffer Ensure(int quads)
    {
        if (_current is not null && quads <= _capacity) return _current;

        int capacity = Math.Max(_capacity * 2, 1 << 15);
        while (capacity < quads) capacity *= 2;
        var data = new uint[capacity * 6];
        for (int q = 0; q < capacity; q++)
        {
            uint v = (uint)q * 4;
            int i = q * 6;
            data[i] = v;
            data[i + 1] = v + 1;
            data[i + 2] = v + 2;
            data[i + 3] = v + 2;
            data[i + 4] = v + 3;
            data[i + 5] = v;
        }

        // Arrays already pointing at a smaller buffer keep it, so it lives until the world is disposed.
        _current = new IndexBuffer(data);
        _buffers.Add(_current);
        _capacity = capacity;
        return _current;
    }

    public void Dispose()
    {
        foreach (IndexBuffer buffer in _buffers) buffer.Dispose();
        _buffers.Clear();
        _current = null;
        _capacity = 0;
    }
}
