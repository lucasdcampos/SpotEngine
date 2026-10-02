namespace Spot.Rendering;

/// <summary>
/// A buffer of vertex data together with the layout describing how each vertex is structured.
/// </summary>
public sealed class VertexBuffer : IDisposable
{
    private readonly GraphicsBuffer<float> _buffer;

    /// <summary>
    /// Initializes a new static <see cref="VertexBuffer"/> and uploads the vertices once.
    /// </summary>
    /// <param name="vertices">The vertex data, laid out according to <paramref name="layout"/>.</param>
    /// <param name="layout">The attributes that make up a single vertex, in order.</param>
    public VertexBuffer(ReadOnlySpan<float> vertices, params ShaderDataType[] layout)
    {
        _buffer = new GraphicsBuffer<float>(vertices, BufferKind.Vertex);
        Layout = layout;
    }

    /// <summary>
    /// Initializes a new dynamic <see cref="VertexBuffer"/> with room for <paramref name="capacityInFloats"/>
    /// floats, to be filled each frame with <see cref="SetData"/>.
    /// </summary>
    /// <param name="capacityInFloats">The number of floats the buffer can hold.</param>
    /// <param name="layout">The attributes that make up a single vertex, in order.</param>
    public VertexBuffer(uint capacityInFloats, params ShaderDataType[] layout)
    {
        _buffer = new GraphicsBuffer<float>(capacityInFloats, BufferKind.Vertex);
        Layout = layout;
    }

    /// <summary>
    /// Gets the attributes that make up a single vertex, in order.
    /// </summary>
    public IReadOnlyList<ShaderDataType> Layout { get; }

    /// <summary>
    /// Gets the size of one vertex in bytes (the sum of the <see cref="Layout"/> attribute sizes).
    /// </summary>
    public uint Stride
    {
        get
        {
            uint stride = 0;
            foreach (ShaderDataType type in Layout)
            {
                stride += type.Size();
            }

            return stride;
        }
    }

    /// <summary>
    /// Gets the underlying GPU buffer, for direct access through <see cref="Renderer.Device"/>.
    /// </summary>
    public GraphicsBuffer<float> Buffer => _buffer;

    /// <summary>
    /// Binds the vertex buffer.
    /// </summary>
    public void Bind() => _buffer.Bind();

    /// <summary>
    /// Replaces the start of the buffer's contents with the given vertex data.
    /// </summary>
    /// <param name="vertices">The vertex data to upload.</param>
    public void SetData(ReadOnlySpan<float> vertices) => _buffer.SetData(vertices);

    /// <inheritdoc />
    public void Dispose() => _buffer.Dispose();
}
