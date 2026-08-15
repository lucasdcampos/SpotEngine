namespace Spot.Rendering;

/// <summary>
/// A typed GPU buffer object (for example a vertex or index buffer). This is a low-level
/// primitive; callers use <see cref="VertexBuffer"/> and <see cref="IndexBuffer"/> instead.
/// </summary>
/// <typeparam name="TData">The unmanaged element type stored in the buffer.</typeparam>
internal sealed class BufferObject<TData> : IDisposable
    where TData : unmanaged
{
    private readonly IGraphicsDevice _device;
    private readonly BufferKind _kind;
    private readonly BufferHandle _handle;

    /// <summary>
    /// Initializes a new immutable buffer and uploads the given data once.
    /// </summary>
    /// <param name="data">The data to upload to the buffer.</param>
    /// <param name="kind">The buffer's role (vertex or index data).</param>
    public BufferObject(ReadOnlySpan<TData> data, BufferKind kind)
    {
        _device = Renderer.Device;
        _kind = kind;
        _handle = _device.CreateBuffer();

        Bind();
        _device.BufferData(_kind, data, BufferUsageKind.StaticDraw);
    }

    /// <summary>
    /// Initializes an empty dynamic buffer sized for <paramref name="capacity"/> elements,
    /// to be filled later with <see cref="SetData"/>.
    /// </summary>
    /// <param name="capacity">The number of elements the buffer can hold.</param>
    /// <param name="kind">The buffer's role (vertex or index data).</param>
    public unsafe BufferObject(uint capacity, BufferKind kind)
    {
        _device = Renderer.Device;
        _kind = kind;
        _handle = _device.CreateBuffer();

        Bind();
        _device.BufferData(_kind, (nuint)(capacity * sizeof(TData)), BufferUsageKind.DynamicDraw);
    }

    /// <summary>
    /// Binds the buffer to the slot for its kind.
    /// </summary>
    public void Bind() => _device.BindBuffer(_kind, _handle);

    /// <summary>
    /// Replaces the start of the buffer's contents with the given data.
    /// </summary>
    /// <param name="data">The data to upload.</param>
    public void SetData(ReadOnlySpan<TData> data)
    {
        Bind();
        _device.BufferSubData(_kind, 0, data);
    }

    /// <inheritdoc />
    public void Dispose() => _device.DeleteBuffer(_handle);
}
