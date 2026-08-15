using Silk.NET.OpenGL;

namespace Spot.Rendering;

/// <summary>
/// The desktop <see cref="IGraphicsDevice"/> backend, implemented against a Silk.NET OpenGL context.
/// </summary>
internal sealed class OpenGLGraphicsDevice : IGraphicsDevice
{
    private readonly GL _gl;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenGLGraphicsDevice"/> class.
    /// </summary>
    /// <param name="gl">The OpenGL API for the active context.</param>
    public OpenGLGraphicsDevice(GL gl) => _gl = gl;

    /// <inheritdoc />
    public void SetClearColor(float r, float g, float b, float a) => _gl.ClearColor(r, g, b, a);

    /// <inheritdoc />
    public void Clear(bool color, bool depth)
    {
        uint mask = 0;
        if (color)
        {
            mask |= (uint)ClearBufferMask.ColorBufferBit;
        }

        if (depth)
        {
            mask |= (uint)ClearBufferMask.DepthBufferBit;
        }

        _gl.Clear(mask);
    }

    /// <inheritdoc />
    public void SetCapability(GraphicsCapability capability, bool enabled)
    {
        EnableCap cap = Map(capability);
        if (enabled)
        {
            _gl.Enable(cap);
        }
        else
        {
            _gl.Disable(cap);
        }
    }

    /// <inheritdoc />
    public void SetViewport(int x, int y, uint width, uint height) => _gl.Viewport(x, y, width, height);

    /// <inheritdoc />
    public void DrawArrays(PrimitiveKind primitive, uint first, uint count) =>
        _gl.DrawArrays(Map(primitive), (int)first, count);

    /// <inheritdoc />
    public unsafe void DrawElements(PrimitiveKind primitive, uint count) =>
        _gl.DrawElements(Map(primitive), count, DrawElementsType.UnsignedInt, null);

    /// <inheritdoc />
    public BufferHandle CreateBuffer() => new(_gl.GenBuffer());

    /// <inheritdoc />
    public void BindBuffer(BufferKind kind, BufferHandle handle) => _gl.BindBuffer(Map(kind), handle.Id);

    /// <inheritdoc />
    public unsafe void BufferData<T>(BufferKind kind, ReadOnlySpan<T> data, BufferUsageKind usage)
        where T : unmanaged
    {
        fixed (T* pData = data)
        {
            _gl.BufferData(Map(kind), (nuint)(data.Length * sizeof(T)), pData, Map(usage));
        }
    }

    /// <inheritdoc />
    public unsafe void BufferData(BufferKind kind, nuint sizeInBytes, BufferUsageKind usage) =>
        _gl.BufferData(Map(kind), sizeInBytes, null, Map(usage));

    /// <inheritdoc />
    public unsafe void BufferSubData<T>(BufferKind kind, nint offsetInBytes, ReadOnlySpan<T> data)
        where T : unmanaged
    {
        fixed (T* pData = data)
        {
            _gl.BufferSubData(Map(kind), offsetInBytes, (nuint)(data.Length * sizeof(T)), pData);
        }
    }

    /// <inheritdoc />
    public void DeleteBuffer(BufferHandle handle) => _gl.DeleteBuffer(handle.Id);

    /// <inheritdoc />
    public VertexArrayHandle CreateVertexArray() => new(_gl.GenVertexArray());

    /// <inheritdoc />
    public void BindVertexArray(VertexArrayHandle handle) => _gl.BindVertexArray(handle.Id);

    /// <inheritdoc />
    public void EnableVertexAttribArray(uint index) => _gl.EnableVertexAttribArray(index);

    /// <inheritdoc />
    public unsafe void VertexAttribPointer(
        uint index, int size, VertexAttribType type, bool normalized, uint stride, nint offset) =>
        _gl.VertexAttribPointer(index, size, Map(type), normalized, stride, (void*)offset);

    /// <inheritdoc />
    public void DeleteVertexArray(VertexArrayHandle handle) => _gl.DeleteVertexArray(handle.Id);

    private static PrimitiveType Map(PrimitiveKind primitive) => primitive switch
    {
        PrimitiveKind.Triangles => PrimitiveType.Triangles,
        PrimitiveKind.TriangleStrip => PrimitiveType.TriangleStrip,
        PrimitiveKind.Lines => PrimitiveType.Lines,
        _ => throw new ArgumentOutOfRangeException(nameof(primitive), primitive, "Unknown primitive kind."),
    };

    private static EnableCap Map(GraphicsCapability capability) => capability switch
    {
        GraphicsCapability.DepthTest => EnableCap.DepthTest,
        GraphicsCapability.CullFace => EnableCap.CullFace,
        GraphicsCapability.Blend => EnableCap.Blend,
        GraphicsCapability.ScissorTest => EnableCap.ScissorTest,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown capability."),
    };

    private static BufferTargetARB Map(BufferKind kind) => kind switch
    {
        BufferKind.Vertex => BufferTargetARB.ArrayBuffer,
        BufferKind.Index => BufferTargetARB.ElementArrayBuffer,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown buffer kind."),
    };

    private static BufferUsageARB Map(BufferUsageKind usage) => usage switch
    {
        BufferUsageKind.StaticDraw => BufferUsageARB.StaticDraw,
        BufferUsageKind.DynamicDraw => BufferUsageARB.DynamicDraw,
        _ => throw new ArgumentOutOfRangeException(nameof(usage), usage, "Unknown buffer usage."),
    };

    private static VertexAttribPointerType Map(VertexAttribType type) => type switch
    {
        VertexAttribType.Float => VertexAttribPointerType.Float,
        VertexAttribType.Int => VertexAttribPointerType.Int,
        VertexAttribType.UnsignedByte => VertexAttribPointerType.UnsignedByte,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown vertex attribute type."),
    };
}
