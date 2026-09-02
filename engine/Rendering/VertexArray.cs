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
    internal uint IndexCount => _indexBuffer?.Count ?? 0;

    /// <summary>
    /// Binds the vertex array.
    /// </summary>
    public void Bind() => _device.BindVertexArray(_handle);

    /// <summary>
    /// Adds a vertex buffer, configuring a vertex attribute for each element of its layout.
    /// </summary>
    /// <param name="vertexBuffer">The vertex buffer to add.</param>
    public void AddVertexBuffer(VertexBuffer vertexBuffer)
    {
        Bind();
        vertexBuffer.Bind();

        uint stride = 0;
        foreach (ShaderDataType type in vertexBuffer.Layout)
        {
            stride += type.Size();
        }

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

            offset += (int)type.Size();
            _attributeIndex++;
        }
    }

    /// <summary>
    /// Adds a per-instance vertex buffer: like <see cref="AddVertexBuffer"/>, but each attribute advances
    /// once per instance (or every <paramref name="divisor"/> instances) rather than per vertex, feeding an
    /// instanced draw. Its attributes follow those already added, so add the geometry buffer first.
    /// </summary>
    /// <param name="vertexBuffer">The buffer of per-instance data.</param>
    /// <param name="divisor">The attribute advance rate: 1 per instance (the default), or every N instances.</param>
    public void AddInstancedVertexBuffer(VertexBuffer vertexBuffer, uint divisor = 1)
    {
        Bind();
        vertexBuffer.Bind();

        uint stride = 0;
        foreach (ShaderDataType type in vertexBuffer.Layout)
        {
            stride += type.Size();
        }

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
            _device.VertexAttribDivisor(_attributeIndex, divisor);

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
