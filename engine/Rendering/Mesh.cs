using Spot.Animation;

namespace Spot.Rendering;

/// <summary>
/// The CPU-side geometry of a mesh: interleaved vertex data and triangle indices, before any GPU
/// upload. Producing this is pure computation (no GL calls), so it is safe to build off the main
/// thread — for example while importing a large model in the background. Turn it into a drawable
/// <see cref="Mesh"/> on the render thread by passing its arrays to the <see cref="Mesh"/> constructor.
/// </summary>
/// <remarks>
/// When <see cref="Skinned"/> is set the vertices carry the 16-float skinning layout and <see cref="Bones"/>
/// lists the submesh's bones (the vertex bone indices point into it).
/// </remarks>
public readonly struct MeshData
{
    /// <summary>Initializes rigid CPU geometry from interleaved vertices and triangle indices.</summary>
    /// <param name="vertices">Interleaved vertex data laid out as position (3), normal (3), texture coordinate (2).</param>
    /// <param name="indices">Triangle indices into <paramref name="vertices"/>.</param>
    public MeshData(float[] vertices, uint[] indices)
        : this(vertices, indices, skinned: false, bones: null)
    {
    }

    /// <summary>Initializes CPU geometry, optionally skinned, from interleaved vertices and triangle indices.</summary>
    /// <param name="vertices">
    /// Interleaved vertex data: position (3), normal (3), texture coordinate (2), and — when
    /// <paramref name="skinned"/> is set — four bone indices (as floats) and four bone weights.
    /// </param>
    /// <param name="indices">Triangle indices into <paramref name="vertices"/>.</param>
    /// <param name="skinned">Whether the vertices carry the skinning layout.</param>
    /// <param name="bones">The submesh's bones (the vertex bone indices point into this list), or <see langword="null"/>.</param>
    public MeshData(float[] vertices, uint[] indices, bool skinned, IReadOnlyList<BoneInfo>? bones)
    {
        Vertices = vertices;
        Indices = indices;
        Skinned = skinned;
        Bones = bones;
    }

    /// <summary>Gets the interleaved vertex data (position, normal, texture coordinate, and skinning when skinned).</summary>
    public float[] Vertices { get; }

    /// <summary>Gets the triangle indices into <see cref="Vertices"/>.</summary>
    public uint[] Indices { get; }

    /// <summary>Gets whether the vertices use the skinned layout (bone indices + weights).</summary>
    public bool Skinned { get; }

    /// <summary>Gets the submesh's bones (indexed by the vertex bone indices), or <see langword="null"/> when rigid.</summary>
    public IReadOnlyList<BoneInfo>? Bones { get; }
}

/// <summary>
/// A drawable triangle mesh on the GPU: interleaved vertices together with an index buffer.
/// </summary>
/// <remarks>
/// This is the low-level 3D primitive. Build one directly from raw vertex/index data (for example
/// procedural geometry) and draw it with <see cref="Renderer3D"/>, or drop down to
/// <see cref="Renderer.DrawIndexed(VertexArray, uint)"/> for full control. A rigid vertex is
/// <see cref="FloatsPerVertex"/> floats: position (x, y, z), normal (x, y, z), texture coordinate (u, v).
/// A skinned vertex is <see cref="SkinnedFloatsPerVertex"/> floats: the same eight plus four bone indices
/// (stored as floats) and four bone weights.
/// </remarks>
public sealed class Mesh : IDisposable
{
    /// <summary>The number of floats that make up a single rigid vertex: position + normal + texture coordinate.</summary>
    public const int FloatsPerVertex = 3 + 3 + 2;

    /// <summary>The number of floats in a skinned vertex: the rigid layout plus bone indices (4) and weights (4).</summary>
    public const int SkinnedFloatsPerVertex = FloatsPerVertex + 4 + 4;

    private readonly VertexArray _vao;
    private readonly VertexBuffer _vbo;
    private readonly IndexBuffer _ibo;

    /// <summary>
    /// Initializes a new rigid <see cref="Mesh"/> and uploads its data to the GPU.
    /// </summary>
    /// <param name="vertices">Interleaved vertex data laid out as position (3), normal (3), texture coordinate (2).</param>
    /// <param name="indices">Triangle indices into <paramref name="vertices"/>.</param>
    public Mesh(ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices)
        : this(vertices, indices, skinned: false)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="Mesh"/> and uploads its data to the GPU, choosing the rigid or skinned
    /// vertex layout.
    /// </summary>
    /// <param name="vertices">
    /// Interleaved vertex data: position (3), normal (3), texture coordinate (2), and — when
    /// <paramref name="skinned"/> is set — four bone indices (as floats) and four bone weights.
    /// </param>
    /// <param name="indices">Triangle indices into <paramref name="vertices"/>.</param>
    /// <param name="skinned">Whether the vertices carry the skinning layout (bone indices at attribute 3, weights at 4).</param>
    public Mesh(ReadOnlySpan<float> vertices, ReadOnlySpan<uint> indices, bool skinned)
    {
        IsSkinned = skinned;
        _vao = new VertexArray();
        _vbo = skinned
            ? new VertexBuffer(
                vertices,
                ShaderDataType.Float3,  // position
                ShaderDataType.Float3,  // normal
                ShaderDataType.Float2,  // texture coordinate
                ShaderDataType.Float4,  // bone indices (stored as floats)
                ShaderDataType.Float4)  // bone weights
            : new VertexBuffer(
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

    /// <summary>Gets whether this mesh uses the skinned vertex layout (bone indices + weights).</summary>
    public bool IsSkinned { get; }

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
