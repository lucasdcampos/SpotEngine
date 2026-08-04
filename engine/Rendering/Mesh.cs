namespace Spot.Rendering;

/// <summary>
/// The CPU-side geometry of a mesh: interleaved vertex data and triangle indices, before any GPU
/// upload. Producing this is pure computation (no GL calls), so it is safe to build off the main
/// thread — for example while importing a large model in the background. Turn it into a drawable
/// <see cref="Mesh"/> on the render thread by passing its arrays to the <see cref="Mesh"/> constructor.
/// </summary>
public readonly struct MeshData
{
    /// <summary>Initializes CPU geometry from interleaved vertices and triangle indices.</summary>
    /// <param name="vertices">Interleaved vertex data laid out as position (3), normal (3), texture coordinate (2).</param>
    /// <param name="indices">Triangle indices into <paramref name="vertices"/>.</param>
    public MeshData(float[] vertices, uint[] indices)
    {
        Vertices = vertices;
        Indices = indices;
    }

    /// <summary>Gets the interleaved vertex data (position, normal, texture coordinate).</summary>
    public float[] Vertices { get; }

    /// <summary>Gets the triangle indices into <see cref="Vertices"/>.</summary>
    public uint[] Indices { get; }
}

/// <summary>
/// A drawable triangle mesh on the GPU: interleaved vertices together with an index buffer.
/// </summary>
/// <remarks>
/// This is the low-level 3D primitive. Build one directly from raw vertex/index data (for example
/// procedural geometry) and draw it with <see cref="Renderer3D"/>, or drop down to
/// <see cref="Renderer.DrawIndexed(VertexArray, uint)"/> for full control. A single vertex is
/// <see cref="FloatsPerVertex"/> floats: position (x, y, z), normal (x, y, z), texture coordinate (u, v).
/// </remarks>
public sealed class Mesh : IDisposable
{
    /// <summary>The number of floats that make up a single vertex: position + normal + texture coordinate.</summary>
    public const int FloatsPerVertex = 3 + 3 + 2;

    private readonly VertexArray _vao;
    private readonly VertexBuffer _vbo;
    private readonly IndexBuffer _ibo;

    /// <summary>
    /// Initializes a new <see cref="Mesh"/> and uploads its data to the GPU.
    /// </summary>
    /// <param name="vertices">Interleaved vertex data laid out as position (3), normal (3), texture coordinate (2).</param>
    /// <param name="indices">Triangle indices into <paramref name="vertices"/>.</param>
    public Mesh(ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices)
    {
        _vao = new VertexArray();
        _vbo = new VertexBuffer(
            vertices,
            ShaderDataType.Float3,
            ShaderDataType.Float3,
            ShaderDataType.Float2);
        _vao.AddVertexBuffer(_vbo);

        _ibo = new IndexBuffer(indices);
        _vao.SetIndexBuffer(_ibo);

        IndexCount = _ibo.Count;
    }

    /// <summary>Gets the number of indices to draw.</summary>
    public uint IndexCount { get; }

    /// <summary>Gets the vertex array backing this mesh, for issuing draw calls.</summary>
    internal VertexArray VertexArray => _vao;

    /// <inheritdoc />
    public void Dispose()
    {
        _vao.Dispose();
        _vbo.Dispose();
        _ibo.Dispose();
    }
}
