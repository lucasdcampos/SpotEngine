namespace Spot.Rendering;

/// <summary>
/// A vertex array object that binds vertex buffers together with their attribute layout
/// and an optional index buffer.
/// </summary>
public sealed class VertexArray : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly VertexArrayHandle _handle;
    private uint _attributeIndex;
    private IndexBuffer? _indexBuffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="VertexArray"/> class and binds it.
    /// </summary>
    public VertexArray()
    {
        _device = Renderer.Device;
        _handle = _device.CreateVertexArray();
        Bind();
    }

    /// <summary>
    /// Gets the number of indices in the attached index buffer, or zero if none is set.
    /// </summary>
    public uint IndexCount => _indexBuffer?.Count ?? 0;

    /// <summary>
    /// Gets the attached index buffer, or <see langword="null"/> if none is set.
    /// </summary>
    public IndexBuffer? IndexBuffer => _indexBuffer;

    /// <summary>
    /// Gets the number of vertex attributes configured so far; the next buffer added starts at this
    /// attribute index (the <c>layout (location = N)</c> its shader inputs must use).
    /// </summary>
    public uint AttributeCount => _attributeIndex;

    /// <summary>
    /// Gets the raw device handle, for issuing commands the wrapper does not expose.
    /// </summary>
    public VertexArrayHandle Handle => _handle;

    /// <summary>
    /// Binds the vertex array.
    /// </summary>
    public void Bind() => _device.BindVertexArray(_handle);

    /// <summary>
    /// Adds a vertex buffer, configuring a vertex attribute for each element of its layout.
    /// </summary>
    /// <param name="vertexBuffer">The vertex buffer to add.</param>
    public void AddVertexBuffer(VertexBuffer vertexBuffer) => AddAttributes(vertexBuffer, divisor: 0);

    /// <summary>
    /// Adds a per-instance vertex buffer: like <see cref="AddVertexBuffer"/>, but each attribute advances
    /// once per instance (or every <paramref name="divisor"/> instances) rather than per vertex, feeding an
    /// instanced draw. Its attributes follow those already added, so add the geometry buffer first.
    /// </summary>
    /// <param name="vertexBuffer">The buffer of per-instance data.</param>
    /// <param name="divisor">The attribute advance rate: 1 per instance (the default), or every N instances.</param>
    public void AddInstancedVertexBuffer(VertexBuffer vertexBuffer, uint divisor = 1) =>
        AddAttributes(vertexBuffer, divisor);

    // Configures one attribute per layout element, packed tightly in layout order. A zero divisor leaves the
    // attribute per-vertex (the device default), so only instanced buffers issue the divisor call.
    private void AddAttributes(VertexBuffer vertexBuffer, uint divisor)
    {
        Bind();
        vertexBuffer.Bind();

        uint stride = vertexBuffer.Stride;
        int offset = 0;
        foreach (ShaderDataType type in vertexBuffer.Layout)
        {
            _device.EnableVertexAttribArray(_attributeIndex);
            _device.VertexAttribPointer(
                _attributeIndex,
                type.ComponentCount(),
                type.ToVertexAttribType(),
                false,
                stride,
                offset);
            if (divisor != 0)
            {
                _device.VertexAttribDivisor(_attributeIndex, divisor);
            }

            offset += (int)type.Size();
            _attributeIndex++;
        }
    }

    /// <summary>
    /// Attaches an index buffer for indexed drawing.
    /// </summary>
    /// <param name="indexBuffer">The index buffer to attach.</param>
    public void SetIndexBuffer(IndexBuffer indexBuffer)
    {
        Bind();
        indexBuffer.Bind();
        _indexBuffer = indexBuffer;
    }

    /// <inheritdoc />
    public void Dispose() => _device.DeleteVertexArray(_handle);
}
