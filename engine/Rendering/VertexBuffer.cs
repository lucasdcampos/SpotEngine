using Silk.NET.OpenGL;

namespace Spot.Rendering;

/// <summary>
/// A buffer of vertex data together with the layout describing how each vertex is structured.
/// </summary>
public sealed class VertexBuffer : IDisposable
{
    private readonly BufferObject<float> _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="VertexBuffer"/> class and uploads the vertices.
    /// </summary>
    /// <param name="vertices">The vertex data, laid out according to <paramref name="layout"/>.</param>
    /// <param name="layout">The attributes that make up a single vertex, in order.</param>
    public VertexBuffer(ReadOnlySpan<float> vertices, params ShaderDataType[] layout)
    {
        _buffer = new BufferObject<float>(vertices, BufferTargetARB.ArrayBuffer);
        Layout = layout;
    }

    /// <summary>
    /// Gets the attributes that make up a single vertex, in order.
    /// </summary>
    internal ShaderDataType[] Layout { get; }

    /// <summary>
    /// Binds the vertex buffer.
    /// </summary>
    public void Bind() => _buffer.Bind();

    /// <inheritdoc />
    public void Dispose() => _buffer.Dispose();
}
