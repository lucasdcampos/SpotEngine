using Silk.NET.OpenGL;

namespace Spot.Rendering;

/// <summary>
/// A typed OpenGL buffer object (for example a vertex or index buffer). This is a low-level
/// primitive; callers use <see cref="VertexBuffer"/> and <see cref="IndexBuffer"/> instead.
/// </summary>
/// <typeparam name="TData">The unmanaged element type stored in the buffer.</typeparam>
internal sealed class BufferObject<TData> : IDisposable
    where TData : unmanaged
{
    private readonly GL _gl;
    private readonly BufferTargetARB _target;
    private readonly uint _handle;

    /// <summary>
    /// Initializes a new immutable buffer and uploads the given data once.
    /// </summary>
    /// <param name="data">The data to upload to the buffer.</param>
    /// <param name="target">The buffer target (for example <see cref="BufferTargetARB.ArrayBuffer"/>).</param>
    public unsafe BufferObject(ReadOnlySpan<TData> data, BufferTargetARB target)
    {
        _gl = Renderer.Gl;
        _target = target;
        _handle = _gl.GenBuffer();

        Bind();
        fixed (TData* pData = data)
        {
            _gl.BufferData(
                _target,
                (nuint)(data.Length * sizeof(TData)),
                pData,
                BufferUsageARB.StaticDraw);
        }
    }

    /// <summary>
    /// Initializes an empty dynamic buffer sized for <paramref name="capacity"/> elements,
    /// to be filled later with <see cref="SetData"/>.
    /// </summary>
    /// <param name="capacity">The number of elements the buffer can hold.</param>
    /// <param name="target">The buffer target (for example <see cref="BufferTargetARB.ArrayBuffer"/>).</param>
    public unsafe BufferObject(uint capacity, BufferTargetARB target)
    {
        _gl = Renderer.Gl;
        _target = target;
        _handle = _gl.GenBuffer();

        Bind();
        _gl.BufferData(_target, (nuint)(capacity * sizeof(TData)), null, BufferUsageARB.DynamicDraw);
    }

    /// <summary>
    /// Binds the buffer to its target.
    /// </summary>
    public void Bind() => _gl.BindBuffer(_target, _handle);

    /// <summary>
    /// Replaces the start of the buffer's contents with the given data.
    /// </summary>
    /// <param name="data">The data to upload.</param>
    public unsafe void SetData(ReadOnlySpan<TData> data)
    {
        Bind();
        fixed (TData* pData = data)
        {
            _gl.BufferSubData(_target, 0, (nuint)(data.Length * sizeof(TData)), pData);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gl.DeleteBuffer(_handle);
}
