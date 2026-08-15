using System.Numerics;
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
    public void SetBlendFunc(BlendFactor source, BlendFactor destination) =>
        _gl.BlendFunc(Map(source), Map(destination));

    /// <inheritdoc />
    public void SetScissor(int x, int y, uint width, uint height) => _gl.Scissor(x, y, width, height);

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

    /// <inheritdoc />
    public string PreprocessShaderSource(ShaderStage stage, string source) => source;

    /// <inheritdoc />
    public ShaderHandle CreateShader(ShaderStage stage) => new(_gl.CreateShader(Map(stage)));

    /// <inheritdoc />
    public void ShaderSource(ShaderHandle shader, string source) => _gl.ShaderSource(shader.Id, source);

    /// <inheritdoc />
    public void CompileShader(ShaderHandle shader) => _gl.CompileShader(shader.Id);

    /// <inheritdoc />
    public bool GetShaderCompileStatus(ShaderHandle shader)
    {
        _gl.GetShader(shader.Id, ShaderParameterName.CompileStatus, out int compiled);
        return compiled != 0;
    }

    /// <inheritdoc />
    public string GetShaderInfoLog(ShaderHandle shader) => _gl.GetShaderInfoLog(shader.Id);

    /// <inheritdoc />
    public ProgramHandle CreateProgram() => new(_gl.CreateProgram());

    /// <inheritdoc />
    public void AttachShader(ProgramHandle program, ShaderHandle shader) => _gl.AttachShader(program.Id, shader.Id);

    /// <inheritdoc />
    public void LinkProgram(ProgramHandle program) => _gl.LinkProgram(program.Id);

    /// <inheritdoc />
    public bool GetProgramLinkStatus(ProgramHandle program)
    {
        _gl.GetProgram(program.Id, ProgramPropertyARB.LinkStatus, out int linked);
        return linked != 0;
    }

    /// <inheritdoc />
    public string GetProgramInfoLog(ProgramHandle program) => _gl.GetProgramInfoLog(program.Id);

    /// <inheritdoc />
    public void DetachShader(ProgramHandle program, ShaderHandle shader) => _gl.DetachShader(program.Id, shader.Id);

    /// <inheritdoc />
    public void DeleteShader(ShaderHandle shader) => _gl.DeleteShader(shader.Id);

    /// <inheritdoc />
    public void DeleteProgram(ProgramHandle program) => _gl.DeleteProgram(program.Id);

    /// <inheritdoc />
    public void UseProgram(ProgramHandle program) => _gl.UseProgram(program.Id);

    /// <inheritdoc />
    public UniformLocation GetUniformLocation(ProgramHandle program, string name) =>
        new(_gl.GetUniformLocation(program.Id, name));

    /// <inheritdoc />
    public void SetUniform(UniformLocation location, int value) => _gl.Uniform1(location.Location, value);

    /// <inheritdoc />
    public void SetUniform(UniformLocation location, float value) => _gl.Uniform1(location.Location, value);

    /// <inheritdoc />
    public void SetUniform(UniformLocation location, Vector2 value) =>
        _gl.Uniform2(location.Location, value.X, value.Y);

    /// <inheritdoc />
    public void SetUniform(UniformLocation location, Vector3 value) =>
        _gl.Uniform3(location.Location, value.X, value.Y, value.Z);

    /// <inheritdoc />
    public void SetUniform(UniformLocation location, Vector4 value) =>
        _gl.Uniform4(location.Location, value.X, value.Y, value.Z, value.W);

    /// <inheritdoc />
    public unsafe void SetUniformMatrix4(UniformLocation location, ReadOnlySpan<Matrix4x4> values)
    {
        if (values.IsEmpty)
        {
            return;
        }

        fixed (Matrix4x4* ptr = values)
        {
            _gl.UniformMatrix4(location.Location, (uint)values.Length, false, (float*)ptr);
        }
    }

    /// <inheritdoc />
    public TextureHandle CreateTexture() => new(_gl.GenTexture());

    /// <inheritdoc />
    public void BindTexture(uint unit, TextureHandle handle)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + (int)unit);
        _gl.BindTexture(TextureTarget.Texture2D, handle.Id);
    }

    /// <inheritdoc />
    public void SetTextureWrap(TextureWrap wrap)
    {
        int value = (int)Map(wrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, value);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, value);
    }

    /// <inheritdoc />
    public void SetTextureFilter(TextureFilter minFilter, TextureFilter magFilter)
    {
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)Map(minFilter));
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)Map(magFilter));
    }

    /// <inheritdoc />
    public float GetMaxAnisotropy()
    {
        // 0x84FF = GL_MAX_TEXTURE_MAX_ANISOTROPY. Left at 1 (a no-op level) where the extension is absent.
        Span<float> max = stackalloc float[1];
        max[0] = 1.0f;
        _gl.GetFloat((GLEnum)0x84FF, max);
        return Math.Max(1.0f, max[0]);
    }

    /// <inheritdoc />
    public void SetTextureMaxAnisotropy(float anisotropy) =>
        // 0x84FE = GL_TEXTURE_MAX_ANISOTROPY.
        _gl.TexParameter(TextureTarget.Texture2D, (GLEnum)0x84FE, anisotropy);

    /// <inheritdoc />
    public unsafe void TextureImage2DRgba8(uint width, uint height, ReadOnlySpan<byte> rgba)
    {
        fixed (byte* pixels = rgba)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                width,
                height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels);
        }
    }

    /// <inheritdoc />
    public void GenerateMipmap2D() => _gl.GenerateMipmap(TextureTarget.Texture2D);

    /// <inheritdoc />
    public void DeleteTexture(TextureHandle handle) => _gl.DeleteTexture(handle.Id);

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

    private static ShaderType Map(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => ShaderType.VertexShader,
        ShaderStage.Fragment => ShaderType.FragmentShader,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown shader stage."),
    };

    private static GLEnum Map(TextureFilter filter) => filter switch
    {
        TextureFilter.Nearest => GLEnum.Nearest,
        TextureFilter.Linear => GLEnum.Linear,
        TextureFilter.LinearMipmapLinear => GLEnum.LinearMipmapLinear,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown texture filter."),
    };

    private static GLEnum Map(TextureWrap wrap) => wrap switch
    {
        TextureWrap.Repeat => GLEnum.Repeat,
        TextureWrap.ClampToEdge => GLEnum.ClampToEdge,
        _ => throw new ArgumentOutOfRangeException(nameof(wrap), wrap, "Unknown texture wrap."),
    };

    private static BlendingFactor Map(BlendFactor factor) => factor switch
    {
        BlendFactor.Zero => BlendingFactor.Zero,
        BlendFactor.One => BlendingFactor.One,
        BlendFactor.SrcAlpha => BlendingFactor.SrcAlpha,
        BlendFactor.OneMinusSrcAlpha => BlendingFactor.OneMinusSrcAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, "Unknown blend factor."),
    };
}
