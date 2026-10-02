namespace Spot.Framework.Graphics;

/// <summary>
/// A buffer of indices used for indexed drawing.
/// </summary>
public sealed class IndexBuffer : IDisposable
{
    private readonly GraphicsBuffer<uint> _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexBuffer"/> class and uploads the indices.
    /// </summary>
    /// <param name="indices">The indices into the associated vertex buffer.</param>
    public IndexBuffer(ReadOnlySpan<uint> indices)
    {
        _buffer = new GraphicsBuffer<uint>(indices, BufferKind.Index);
        Count = (uint)indices.Length;
    }

    /// <summary>
    /// Gets the number of indices in the buffer.
    /// </summary>
    public uint Count { get; }

    /// <summary>
    /// Gets the underlying GPU buffer, for direct access through <see cref="Renderer.Device"/>.
    /// </summary>
    public GraphicsBuffer<uint> Buffer => _buffer;

    /// <summary>
    /// Binds the index buffer.
    /// </summary>
    public void Bind() => _buffer.Bind();

    /// <inheritdoc />
    public void Dispose() => _buffer.Dispose();
}
