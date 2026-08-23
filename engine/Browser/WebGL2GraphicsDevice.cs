using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace Spot.Rendering;

/// <summary>
/// The browser <see cref="IGraphicsDevice"/> backend: it drives a real WebGL2 context from C# over
/// <c>[JSImport]</c>. Every GL command is a call into a thin JavaScript surface (registered by the browser
/// host under the <c>spot-gl</c> module) that forwards to the canvas' WebGL2 context and keeps an integer
/// handle table for GL objects — so C# refers to buffers, shaders, programs, and textures by <c>int</c>,
/// exactly as the desktop backend refers to them by GL name.
/// </summary>
/// <remarks>
/// WebGL2 is OpenGL ES 3.0, so engine shaders authored in desktop GLSL are rewritten to <c>#version 300 es</c>
/// by <see cref="PreprocessShaderSource"/> before compilation. Enum values are mapped to their numeric
/// WebGL/GLES constants on the C# side (they are identical to desktop GL), keeping the JS surface a thin
/// pass-through.
///
/// <para>The JavaScript host must register the <c>spot-gl</c> module (via <c>setModuleImports</c>) with a
/// <c>gl</c> object implementing each imported function below over the live <c>WebGL2RenderingContext</c>.</para>
/// </remarks>
internal sealed partial class WebGL2GraphicsDevice : IGraphicsDevice
{
    private const string Module = "spot-gl";

    /// <inheritdoc />
    public void SetClearColor(float r, float g, float b, float a) => JsClearColor(r, g, b, a);

    /// <inheritdoc />
    public void Clear(bool color, bool depth)
    {
        int mask = 0;
        if (color)
        {
            mask |= GLc.ColorBufferBit;
        }

        if (depth)
        {
            mask |= GLc.DepthBufferBit;
        }

        JsClear(mask);
    }

    /// <inheritdoc />
    public void SetCapability(GraphicsCapability capability, bool enabled)
    {
        int cap = MapCapability(capability);
        if (enabled)
        {
            JsEnable(cap);
        }
        else
        {
            JsDisable(cap);
        }
    }

    /// <inheritdoc />
    public void SetDepthWrite(bool write) => JsDepthMask(write);

    /// <inheritdoc />
    public void SetViewport(int x, int y, uint width, uint height) => JsViewport(x, y, (int)width, (int)height);

    /// <inheritdoc />
    public void SetBlendFunc(BlendFactor source, BlendFactor destination) =>
        JsBlendFunc(MapBlend(source), MapBlend(destination));

    /// <inheritdoc />
    public void SetScissor(int x, int y, uint width, uint height) => JsScissor(x, y, (int)width, (int)height);

    /// <inheritdoc />
    public void DrawArrays(PrimitiveKind primitive, uint first, uint count) =>
        JsDrawArrays(MapPrimitive(primitive), (int)first, (int)count);

    /// <inheritdoc />
    public void DrawElements(PrimitiveKind primitive, uint count) =>
        JsDrawElements(MapPrimitive(primitive), (int)count, GLc.UnsignedInt, 0);

    /// <inheritdoc />
    public BufferHandle CreateBuffer() => new((uint)JsCreateBuffer());

    /// <inheritdoc />
    public void BindBuffer(BufferKind kind, BufferHandle handle) => JsBindBuffer(MapBuffer(kind), (int)handle.Id);

    /// <inheritdoc />
    public void BufferData<T>(BufferKind kind, ReadOnlySpan<T> data, BufferUsageKind usage)
        where T : unmanaged =>
        JsBufferData(MapBuffer(kind), AsWritableBytes(data), MapUsage(usage));

    /// <inheritdoc />
    public void BufferData(BufferKind kind, nuint sizeInBytes, BufferUsageKind usage) =>
        JsBufferDataSize(MapBuffer(kind), (int)sizeInBytes, MapUsage(usage));

    /// <inheritdoc />
    public void BufferSubData<T>(BufferKind kind, nint offsetInBytes, ReadOnlySpan<T> data)
        where T : unmanaged =>
        JsBufferSubData(MapBuffer(kind), (int)offsetInBytes, AsWritableBytes(data));

    /// <inheritdoc />
    public void DeleteBuffer(BufferHandle handle) => JsDeleteBuffer((int)handle.Id);

    /// <inheritdoc />
    public VertexArrayHandle CreateVertexArray() => new((uint)JsCreateVertexArray());

    /// <inheritdoc />
    public void BindVertexArray(VertexArrayHandle handle) => JsBindVertexArray((int)handle.Id);

    /// <inheritdoc />
    public void EnableVertexAttribArray(uint index) => JsEnableVertexAttribArray((int)index);

    /// <inheritdoc />
    public void VertexAttribPointer(
        uint index, int size, VertexAttribType type, bool normalized, uint stride, nint offset) =>
        JsVertexAttribPointer((int)index, size, MapAttrib(type), normalized, (int)stride, (int)offset);

    /// <inheritdoc />
    public void DeleteVertexArray(VertexArrayHandle handle) => JsDeleteVertexArray((int)handle.Id);

    /// <inheritdoc />
    public string PreprocessShaderSource(ShaderStage stage, string source)
    {
        _ = stage;
        return GlslTranspiler.ToGlslEs300(source);
    }

    /// <inheritdoc />
    public ShaderHandle CreateShader(ShaderStage stage) => new((uint)JsCreateShader(MapStage(stage)));

    /// <inheritdoc />
    public void ShaderSource(ShaderHandle shader, string source) => JsShaderSource((int)shader.Id, source);

    /// <inheritdoc />
    public void CompileShader(ShaderHandle shader) => JsCompileShader((int)shader.Id);

    /// <inheritdoc />
    public bool GetShaderCompileStatus(ShaderHandle shader) => JsGetShaderCompileStatus((int)shader.Id);

    /// <inheritdoc />
    public string GetShaderInfoLog(ShaderHandle shader) => JsGetShaderInfoLog((int)shader.Id);

    /// <inheritdoc />
    public ProgramHandle CreateProgram() => new((uint)JsCreateProgram());

    /// <inheritdoc />
    public void AttachShader(ProgramHandle program, ShaderHandle shader) =>
        JsAttachShader((int)program.Id, (int)shader.Id);

    /// <inheritdoc />
    public void LinkProgram(ProgramHandle program) => JsLinkProgram((int)program.Id);

    /// <inheritdoc />
    public bool GetProgramLinkStatus(ProgramHandle program) => JsGetProgramLinkStatus((int)program.Id);

    /// <inheritdoc />
    public string GetProgramInfoLog(ProgramHandle program) => JsGetProgramInfoLog((int)program.Id);

    /// <inheritdoc />
    public void DetachShader(ProgramHandle program, ShaderHandle shader) =>
        JsDetachShader((int)program.Id, (int)shader.Id);

    /// <inheritdoc />
    public void DeleteShader(ShaderHandle shader) => JsDeleteShader((int)shader.Id);

    /// <inheritdoc />
    public void DeleteProgram(ProgramHandle program) => JsDeleteProgram((int)program.Id);

    /// <inheritdoc />
    public void UseProgram(ProgramHandle program) => JsUseProgram((int)program.Id);

    /// <inheritdoc />
    public UniformLocation GetUniformLocation(ProgramHandle program, string name) =>
        new(JsGetUniformLocation((int)program.Id, name));

    /// <inheritdoc />
    public void SetUniform(UniformLocation location, int value) => JsUniform1i(location.Location, value);

    /// <inheritdoc />
    public void SetUniform(UniformLocation location, float value) => JsUniform1f(location.Location, value);

    /// <inheritdoc />
    public void SetUniform(UniformLocation location, Vector2 value) =>
        JsUniform2f(location.Location, value.X, value.Y);

    /// <inheritdoc />
    public void SetUniform(UniformLocation location, Vector3 value) =>
        JsUniform3f(location.Location, value.X, value.Y, value.Z);

    /// <inheritdoc />
    public void SetUniform(UniformLocation location, Vector4 value) =>
        JsUniform4f(location.Location, value.X, value.Y, value.Z, value.W);

    /// <inheritdoc />
    public void SetUniformMatrix4(UniformLocation location, ReadOnlySpan<Matrix4x4> values)
    {
        if (values.IsEmpty)
        {
            return;
        }

        JsUniformMatrix4fv(location.Location, AsWritableBytes(values));
    }

    /// <inheritdoc />
    public TextureHandle CreateTexture() => new((uint)JsCreateTexture());

    /// <inheritdoc />
    public void BindTexture(uint unit, TextureHandle handle) => JsBindTexture((int)unit, (int)handle.Id);

    /// <inheritdoc />
    public void SetTextureWrap(TextureWrap wrap)
    {
        int value = MapWrap(wrap);
        JsTexParameteri(GLc.TextureWrapS, value);
        JsTexParameteri(GLc.TextureWrapT, value);
    }

    /// <inheritdoc />
    public void SetTextureFilter(TextureFilter minFilter, TextureFilter magFilter)
    {
        JsTexParameteri(GLc.TextureMinFilter, MapFilter(minFilter));
        JsTexParameteri(GLc.TextureMagFilter, MapFilter(magFilter));
    }

    /// <inheritdoc />
    // Anisotropic filtering is an optional WebGL2 extension; the MVP renders without it (a level of 1 is a
    // no-op), so report 1 and skip the extension plumbing entirely.
    public float GetMaxAnisotropy() => 1.0f;

    /// <inheritdoc />
    public void SetTextureMaxAnisotropy(float anisotropy)
    {
        _ = anisotropy;
    }

    /// <inheritdoc />
    public void TextureImage2DRgba8(uint width, uint height, ReadOnlySpan<byte> rgba) =>
        JsTexImage2DRgba8((int)width, (int)height, AsWritableBytes(rgba));

    /// <inheritdoc />
    public void TextureImage2D(TextureInternalFormat format, uint width, uint height, ReadOnlySpan<byte> data) =>
        JsTexImage2D(MapInternalFormat(format), (int)width, (int)height, AsWritableBytes(data));

    /// <inheritdoc />
    public void SetTextureCompareMode(bool enabled) => JsTexCompareMode(enabled);

    /// <inheritdoc />
    public void GenerateMipmap2D() => JsGenerateMipmap2D();

    /// <inheritdoc />
    public void DeleteTexture(TextureHandle handle) => JsDeleteTexture((int)handle.Id);

    /// <inheritdoc />
    public FramebufferHandle CreateFramebuffer() => new((uint)JsCreateFramebuffer());

    /// <inheritdoc />
    public void BindFramebuffer(FramebufferHandle handle) => JsBindFramebuffer((int)handle.Id);

    /// <inheritdoc />
    public void FramebufferTexture2D(RenderTargetAttachment attachment, TextureHandle texture) =>
        JsFramebufferTexture2D(MapAttachment(attachment), (int)texture.Id);

    /// <inheritdoc />
    public bool CheckFramebufferComplete() => JsCheckFramebufferComplete();

    /// <inheritdoc />
    public void SetColorBuffersNone() => JsSetColorBuffersNone();

    /// <inheritdoc />
    public void BlitDepth(FramebufferHandle source, FramebufferHandle destination, uint width, uint height,
        int destX, int destY, uint destWidth, uint destHeight) =>
        JsBlitDepth((int)source.Id, (int)destination.Id, (int)width, (int)height,
            destX, destY, (int)destWidth, (int)destHeight);

    /// <inheritdoc />
    public void DeleteFramebuffer(FramebufferHandle handle) => JsDeleteFramebuffer((int)handle.Id);

    /// <inheritdoc />
    // WebGL2 has no glPolygonMode; wireframe is unavailable, so this is a no-op (polygons render filled).
    public void SetWireframe(bool enabled)
    {
        _ = enabled;
    }

    // Reinterprets a read-only span as a writable byte span for MemoryView marshaling. The JS side only reads
    // the view within the synchronous call (GL copies immediately), so exposing it as writable is safe.
    private static Span<byte> AsWritableBytes<T>(ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);
        return MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in MemoryMarshal.GetReference(bytes)), bytes.Length);
    }

    private static int MapPrimitive(PrimitiveKind primitive) => primitive switch
    {
        PrimitiveKind.Triangles => GLc.Triangles,
        PrimitiveKind.TriangleStrip => GLc.TriangleStrip,
        PrimitiveKind.Lines => GLc.Lines,
        _ => throw new ArgumentOutOfRangeException(nameof(primitive), primitive, "Unknown primitive kind."),
    };

    private static int MapCapability(GraphicsCapability capability) => capability switch
    {
        GraphicsCapability.DepthTest => GLc.DepthTest,
        GraphicsCapability.CullFace => GLc.CullFace,
        GraphicsCapability.Blend => GLc.Blend,
        GraphicsCapability.ScissorTest => GLc.ScissorTest,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown capability."),
    };

    private static int MapBuffer(BufferKind kind) => kind switch
    {
        BufferKind.Vertex => GLc.ArrayBuffer,
        BufferKind.Index => GLc.ElementArrayBuffer,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown buffer kind."),
    };

    private static int MapUsage(BufferUsageKind usage) => usage switch
    {
        BufferUsageKind.StaticDraw => GLc.StaticDraw,
        BufferUsageKind.DynamicDraw => GLc.DynamicDraw,
        _ => throw new ArgumentOutOfRangeException(nameof(usage), usage, "Unknown buffer usage."),
    };

    private static int MapAttrib(VertexAttribType type) => type switch
    {
        VertexAttribType.Float => GLc.Float,
        VertexAttribType.Int => GLc.Int,
        VertexAttribType.UnsignedByte => GLc.UnsignedByte,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown vertex attribute type."),
    };

    private static int MapStage(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => GLc.VertexShader,
        ShaderStage.Fragment => GLc.FragmentShader,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown shader stage."),
    };

    private static int MapFilter(TextureFilter filter) => filter switch
    {
        TextureFilter.Nearest => GLc.Nearest,
        TextureFilter.Linear => GLc.Linear,
        TextureFilter.LinearMipmapLinear => GLc.LinearMipmapLinear,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown texture filter."),
    };

    private static int MapWrap(TextureWrap wrap) => wrap switch
    {
        TextureWrap.Repeat => GLc.Repeat,
        TextureWrap.ClampToEdge => GLc.ClampToEdge,
        _ => throw new ArgumentOutOfRangeException(nameof(wrap), wrap, "Unknown texture wrap."),
    };

    // Small integer codes (not raw GL enums): the JS side maps each to the right (internalFormat, format, type)
    // triple, since WebGL2's float/depth formats need specific combinations.
    private static int MapInternalFormat(TextureInternalFormat format) => format switch
    {
        TextureInternalFormat.Rgba8 => 0,
        TextureInternalFormat.Rgba16F => 1,
        TextureInternalFormat.DepthComponent32F => 2,
        TextureInternalFormat.Depth24Stencil8 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown texture internal format."),
    };

    private static int MapAttachment(RenderTargetAttachment attachment) => attachment switch
    {
        RenderTargetAttachment.Color0 => 0,
        RenderTargetAttachment.Depth => 1,
        RenderTargetAttachment.DepthStencil => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(attachment), attachment, "Unknown attachment."),
    };

    private static int MapBlend(BlendFactor factor) => factor switch
    {
        BlendFactor.Zero => GLc.Zero,
        BlendFactor.One => GLc.One,
        BlendFactor.SrcAlpha => GLc.SrcAlpha,
        BlendFactor.OneMinusSrcAlpha => GLc.OneMinusSrcAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(factor), factor, "Unknown blend factor."),
    };

    // The numeric WebGL2 / OpenGL ES 3.0 constants used above. They are identical to their desktop GL values,
    // so mapping engine enums here keeps the JavaScript surface a thin pass-through.
    private static class GLc
    {
        public const int ColorBufferBit = 0x4000;
        public const int DepthBufferBit = 0x0100;
        public const int ArrayBuffer = 0x8892;
        public const int ElementArrayBuffer = 0x8893;
        public const int StaticDraw = 0x88E4;
        public const int DynamicDraw = 0x88E8;
        public const int Float = 0x1406;
        public const int Int = 0x1404;
        public const int UnsignedByte = 0x1401;
        public const int UnsignedInt = 0x1405;
        public const int VertexShader = 0x8B31;
        public const int FragmentShader = 0x8B30;
        public const int Triangles = 0x0004;
        public const int TriangleStrip = 0x0005;
        public const int Lines = 0x0001;
        public const int DepthTest = 0x0B71;
        public const int CullFace = 0x0B44;
        public const int Blend = 0x0BE2;
        public const int ScissorTest = 0x0C11;
        public const int Zero = 0;
        public const int One = 1;
        public const int SrcAlpha = 0x0302;
        public const int OneMinusSrcAlpha = 0x0303;
        public const int Nearest = 0x2600;
        public const int Linear = 0x2601;
        public const int LinearMipmapLinear = 0x2703;
        public const int Repeat = 0x2901;
        public const int ClampToEdge = 0x812F;
        public const int TextureMagFilter = 0x2800;
        public const int TextureMinFilter = 0x2801;
        public const int TextureWrapS = 0x2802;
        public const int TextureWrapT = 0x2803;
    }

    // ---- The WebGL2 interop surface, implemented by the host's `spot-gl` JS module over the live context. ----

    [JSImport("gl.viewport", Module)]
    private static partial void JsViewport(int x, int y, int width, int height);

    [JSImport("gl.scissor", Module)]
    private static partial void JsScissor(int x, int y, int width, int height);

    [JSImport("gl.clearColor", Module)]
    private static partial void JsClearColor(double r, double g, double b, double a);

    [JSImport("gl.clear", Module)]
    private static partial void JsClear(int mask);

    [JSImport("gl.enable", Module)]
    private static partial void JsEnable(int cap);

    [JSImport("gl.disable", Module)]
    private static partial void JsDisable(int cap);

    [JSImport("gl.depthMask", Module)]
    private static partial void JsDepthMask(bool flag);

    [JSImport("gl.blendFunc", Module)]
    private static partial void JsBlendFunc(int source, int destination);

    [JSImport("gl.drawArrays", Module)]
    private static partial void JsDrawArrays(int mode, int first, int count);

    [JSImport("gl.drawElements", Module)]
    private static partial void JsDrawElements(int mode, int count, int type, int offset);

    [JSImport("gl.createBuffer", Module)]
    private static partial int JsCreateBuffer();

    [JSImport("gl.bindBuffer", Module)]
    private static partial void JsBindBuffer(int target, int buffer);

    [JSImport("gl.bufferData", Module)]
    private static partial void JsBufferData(int target, [JSMarshalAs<JSType.MemoryView>] Span<byte> data, int usage);

    [JSImport("gl.bufferDataSize", Module)]
    private static partial void JsBufferDataSize(int target, int sizeInBytes, int usage);

    [JSImport("gl.bufferSubData", Module)]
    private static partial void JsBufferSubData(int target, int offset, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    [JSImport("gl.deleteBuffer", Module)]
    private static partial void JsDeleteBuffer(int buffer);

    [JSImport("gl.createVertexArray", Module)]
    private static partial int JsCreateVertexArray();

    [JSImport("gl.bindVertexArray", Module)]
    private static partial void JsBindVertexArray(int vertexArray);

    [JSImport("gl.enableVertexAttribArray", Module)]
    private static partial void JsEnableVertexAttribArray(int index);

    [JSImport("gl.vertexAttribPointer", Module)]
    private static partial void JsVertexAttribPointer(int index, int size, int type, bool normalized, int stride, int offset);

    [JSImport("gl.deleteVertexArray", Module)]
    private static partial void JsDeleteVertexArray(int vertexArray);

    [JSImport("gl.createShader", Module)]
    private static partial int JsCreateShader(int type);

    [JSImport("gl.shaderSource", Module)]
    private static partial void JsShaderSource(int shader, string source);

    [JSImport("gl.compileShader", Module)]
    private static partial void JsCompileShader(int shader);

    [JSImport("gl.getShaderCompileStatus", Module)]
    private static partial bool JsGetShaderCompileStatus(int shader);

    [JSImport("gl.getShaderInfoLog", Module)]
    private static partial string JsGetShaderInfoLog(int shader);

    [JSImport("gl.deleteShader", Module)]
    private static partial void JsDeleteShader(int shader);

    [JSImport("gl.createProgram", Module)]
    private static partial int JsCreateProgram();

    [JSImport("gl.attachShader", Module)]
    private static partial void JsAttachShader(int program, int shader);

    [JSImport("gl.detachShader", Module)]
    private static partial void JsDetachShader(int program, int shader);

    [JSImport("gl.linkProgram", Module)]
    private static partial void JsLinkProgram(int program);

    [JSImport("gl.getProgramLinkStatus", Module)]
    private static partial bool JsGetProgramLinkStatus(int program);

    [JSImport("gl.getProgramInfoLog", Module)]
    private static partial string JsGetProgramInfoLog(int program);

    [JSImport("gl.deleteProgram", Module)]
    private static partial void JsDeleteProgram(int program);

    [JSImport("gl.useProgram", Module)]
    private static partial void JsUseProgram(int program);

    [JSImport("gl.getUniformLocation", Module)]
    private static partial int JsGetUniformLocation(int program, string name);

    [JSImport("gl.uniform1i", Module)]
    private static partial void JsUniform1i(int location, int value);

    [JSImport("gl.uniform1f", Module)]
    private static partial void JsUniform1f(int location, double value);

    [JSImport("gl.uniform2f", Module)]
    private static partial void JsUniform2f(int location, double x, double y);

    [JSImport("gl.uniform3f", Module)]
    private static partial void JsUniform3f(int location, double x, double y, double z);

    [JSImport("gl.uniform4f", Module)]
    private static partial void JsUniform4f(int location, double x, double y, double z, double w);

    [JSImport("gl.uniformMatrix4fv", Module)]
    private static partial void JsUniformMatrix4fv(int location, [JSMarshalAs<JSType.MemoryView>] Span<byte> value);

    [JSImport("gl.createTexture", Module)]
    private static partial int JsCreateTexture();

    [JSImport("gl.bindTexture", Module)]
    private static partial void JsBindTexture(int unit, int texture);

    [JSImport("gl.texParameteri", Module)]
    private static partial void JsTexParameteri(int name, int value);

    [JSImport("gl.texImage2DRgba8", Module)]
    private static partial void JsTexImage2DRgba8(int width, int height, [JSMarshalAs<JSType.MemoryView>] Span<byte> rgba);

    [JSImport("gl.texImage2D", Module)]
    private static partial void JsTexImage2D(int format, int width, int height, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    [JSImport("gl.texCompareMode", Module)]
    private static partial void JsTexCompareMode(bool enabled);

    [JSImport("gl.generateMipmap2D", Module)]
    private static partial void JsGenerateMipmap2D();

    [JSImport("gl.deleteTexture", Module)]
    private static partial void JsDeleteTexture(int texture);

    [JSImport("gl.createFramebuffer", Module)]
    private static partial int JsCreateFramebuffer();

    [JSImport("gl.bindFramebuffer", Module)]
    private static partial void JsBindFramebuffer(int framebuffer);

    [JSImport("gl.framebufferTexture2D", Module)]
    private static partial void JsFramebufferTexture2D(int attachment, int texture);

    [JSImport("gl.checkFramebufferComplete", Module)]
    private static partial bool JsCheckFramebufferComplete();

    [JSImport("gl.setColorBuffersNone", Module)]
    private static partial void JsSetColorBuffersNone();

    [JSImport("gl.blitDepth", Module)]
    private static partial void JsBlitDepth(
        int source, int destination, int width, int height, int destX, int destY, int destWidth, int destHeight);

    [JSImport("gl.deleteFramebuffer", Module)]
    private static partial void JsDeleteFramebuffer(int framebuffer);
}
