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
    /// Initializes a new instance of the <see cref="BufferObject{TData}"/> class and uploads the data.
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
    /// Binds the buffer to its target.
    /// </summary>
    public void Bind() => _gl.BindBuffer(_target, _handle);

    /// <inheritdoc />
    public void Dispose() => _gl.DeleteBuffer(_handle);
}
