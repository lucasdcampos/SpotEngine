namespace Spot.Engine.Graphics;

/// <summary>
/// A typed GPU buffer object: vertex data, element indices, or a uniform block. This is the low-level
/// primitive under <see cref="VertexBuffer"/> and <see cref="IndexBuffer"/>; use it directly for buffers those
/// wrappers do not cover (uniform buffers, custom instance streams, or anything a custom renderer needs).
/// </summary>
/// <typeparam name="TData">The unmanaged element type stored in the buffer.</typeparam>
public sealed class GraphicsBuffer<TData> : IDisposable
    where TData : unmanaged
{
    private readonly IGraphicsDevice _device;
    private BufferHandle _handle;

    /// <summary>
    /// Initializes a new static buffer and uploads the given data once.
    /// </summary>
    /// <param name="data">The data to upload to the buffer.</param>
    /// <param name="kind">The buffer's role.</param>
    public GraphicsBuffer(ReadOnlySpan<TData> data, BufferKind kind)
    {
        _device = Renderer.Device;
        Kind = kind;
        Capacity = (uint)data.Length;
        _handle = _device.CreateBuffer();

        Bind();
        _device.BufferData(Kind, data, BufferUsageKind.StaticDraw);
    }

    /// <summary>
    /// Initializes a new dynamic buffer with uninitialized storage for <paramref name="capacity"/> elements,
    /// to be filled later with <see cref="SetData(ReadOnlySpan{TData})"/>.
    /// </summary>
    /// <param name="capacity">The number of elements the buffer can hold.</param>
    /// <param name="kind">The buffer's role.</param>
    public unsafe GraphicsBuffer(uint capacity, BufferKind kind)
    {
        _device = Renderer.Device;
        Kind = kind;
        Capacity = capacity;
        _handle = _device.CreateBuffer();

        Bind();
        _device.BufferData(Kind, (nuint)(capacity * sizeof(TData)), BufferUsageKind.DynamicDraw);
    }

    /// <summary>Gets the buffer's role.</summary>
    public BufferKind Kind { get; }

    /// <summary>Gets the number of elements the buffer was created to hold.</summary>
    public uint Capacity { get; }

    /// <summary>Gets the raw device handle, for issuing commands the wrapper does not expose.</summary>
    public BufferHandle Handle => _handle;

    /// <summary>
    /// Binds the buffer to the slot for its <see cref="Kind"/>.
    /// </summary>
    public void Bind() => _device.BindBuffer(Kind, _handle);

    /// <summary>
    /// Binds the buffer to an indexed binding point (for a uniform buffer, the binding a shader's uniform
    /// block reads from; see <see cref="Shader.BindUniformBlock"/>).
    /// </summary>
    /// <param name="bindingPoint">The indexed binding point.</param>
    public void BindBase(uint bindingPoint) => _device.BindBufferBase(Kind, bindingPoint, _handle);

    /// <summary>
    /// Replaces the start of the buffer's contents with the given data.
    /// </summary>
    /// <param name="data">The new data.</param>
    public void SetData(ReadOnlySpan<TData> data) => SetData(0, data);

    /// <summary>
    /// Replaces part of the buffer's contents, starting at the given element offset.
    /// </summary>
    /// <param name="elementOffset">The index of the first element to overwrite.</param>
    /// <param name="data">The new data.</param>
    public unsafe void SetData(uint elementOffset, ReadOnlySpan<TData> data)
    {
        Bind();
        _device.BufferSubData(Kind, (nint)(elementOffset * sizeof(TData)), data);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_handle.Id != 0)
        {
            _device.DeleteBuffer(_handle);
            _handle = default;
        }
    }
}
