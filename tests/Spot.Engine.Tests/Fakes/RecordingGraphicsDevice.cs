using System.Numerics;
using System.Runtime.InteropServices;
using Spot.Rendering;

namespace Spot.Engine.Tests.Fakes;

/// <summary>
/// An in-memory <see cref="IGraphicsDevice"/> that records every command instead of talking to a GPU. Installed
/// through <c>Renderer.Init</c>, it lets the rendering layers (buffers, shaders, textures, the 2D batcher) be
/// tested headless: tests assert on the recorded calls, the live resource sets, and the uploaded data.
/// </summary>
internal sealed class RecordingGraphicsDevice : IGraphicsDevice
{
    private uint _nextId = 1;
    private readonly Dictionary<(uint Program, string Name), int> _uniformLocations = new();
    private readonly Dictionary<int, string> _uniformNames = new();
    private uint _boundProgram;

    /// <summary>Every command, by method name, in call order.</summary>
    public List<string> Calls { get; } = new();

    /// <summary>Each draw call issued, in order.</summary>
    public List<DrawCall> Draws { get; } = new();

    public HashSet<uint> LiveBuffers { get; } = new();
    public HashSet<uint> LiveVertexArrays { get; } = new();
    public HashSet<uint> LiveShaders { get; } = new();
    public HashSet<uint> LivePrograms { get; } = new();
    public HashSet<uint> LiveTextures { get; } = new();
    public HashSet<uint> LiveFramebuffers { get; } = new();

    /// <summary>The buffer currently bound to each slot.</summary>
    public Dictionary<BufferKind, uint> BoundBuffers { get; } = new();

    /// <summary>The allocated size (bytes) and usage of each buffer, keyed by handle id.</summary>
    public Dictionary<uint, (nuint Size, BufferUsageKind Usage)> BufferStorage { get; } = new();

    /// <summary>The last bytes written to each buffer and the byte offset they were written at.</summary>
    public Dictionary<uint, (nint Offset, byte[] Bytes)> LastBufferWrite { get; } = new();

    public List<VertexAttribute> Attributes { get; } = new();
    public Dictionary<uint, uint> Divisors { get; } = new();

    public uint BoundVertexArray { get; private set; }
    public uint BoundFramebuffer { get; private set; }
    public Dictionary<uint, uint> BoundTextures { get; } = new();

    /// <summary>The image allocated for each texture, keyed by handle id.</summary>
    public Dictionary<uint, TextureImage> TextureImages { get; } = new();
    public Dictionary<uint, (TextureFilter Min, TextureFilter Mag)> TextureFilters { get; } = new();
    public Dictionary<uint, TextureWrap> TextureWraps { get; } = new();
    public Dictionary<uint, float> TextureAnisotropy { get; } = new();
    public HashSet<uint> MipmappedTextures { get; } = new();
    public Dictionary<uint, Dictionary<RenderTargetAttachment, uint>> FramebufferAttachments { get; } = new();

    /// <summary>Uniform writes, by uniform name, latest value last.</summary>
    public List<(string Name, object Value)> UniformWrites { get; } = new();

    /// <summary>The source each shader received, after preprocessing.</summary>
    public Dictionary<uint, (ShaderStage Stage, string Source)> ShaderSources { get; } = new();

    public Dictionary<GraphicsCapability, bool> Capabilities { get; } = new();
    public (int X, int Y, uint Width, uint Height) Viewport { get; private set; }
    public Vector4 ClearColor { get; private set; }
    public (bool Color, bool Depth)? LastClear { get; private set; }
    public bool DepthWrite { get; private set; } = true;
    public (BlendFactor Source, BlendFactor Destination)? BlendFunc { get; private set; }

    /// <summary>Makes shader compilation fail for the given stage (null: never fail).</summary>
    public ShaderStage? FailCompileStage { get; set; }

    /// <summary>Makes program linking fail.</summary>
    public bool FailLink { get; set; }

    /// <summary>Makes framebuffers report incomplete.</summary>
    public bool FailFramebuffer { get; set; }

    /// <summary>The value <see cref="GetMaxAnisotropy"/> reports.</summary>
    public float MaxAnisotropy { get; set; } = 16.0f;

    /// <summary>Text prepended by <see cref="PreprocessShaderSource"/>, to prove callers pass sources through it.</summary>
    public string PreprocessPrefix { get; set; } = "";

    public bool SupportsUniformBuffers => true;

    /// <summary>Gets the most recent value written to the named uniform, or null if never written.</summary>
    public object? Uniform(string name)
    {
        for (int i = UniformWrites.Count - 1; i >= 0; i--)
        {
            if (UniformWrites[i].Name == name)
            {
                return UniformWrites[i].Value;
            }
        }

        return null;
    }

    /// <summary>Gets the last data written to a buffer, reinterpreted as <typeparamref name="T"/>.</summary>
    public T[] BufferContents<T>(uint buffer) where T : unmanaged =>
        MemoryMarshal.Cast<byte, T>(LastBufferWrite[buffer].Bytes).ToArray();

    public void SetClearColor(float r, float g, float b, float a)
    {
        Record();
        ClearColor = new Vector4(r, g, b, a);
    }

    public void Clear(bool color, bool depth)
    {
        Record();
        LastClear = (color, depth);
    }

    public void SetCapability(GraphicsCapability capability, bool enabled)
    {
        Record();
        Capabilities[capability] = enabled;
    }

    public void SetViewport(int x, int y, uint width, uint height)
    {
        Record();
        Viewport = (x, y, width, height);
    }

    public void SetDepthWrite(bool write)
    {
        Record();
        DepthWrite = write;
    }

    public void SetBlendFunc(BlendFactor source, BlendFactor destination)
    {
        Record();
        BlendFunc = (source, destination);
    }

    /// <summary>The last scissor rectangle set (lower-left origin).</summary>
    public (int X, int Y, uint Width, uint Height)? Scissor { get; private set; }

    public void SetScissor(int x, int y, uint width, uint height)
    {
        Record();
        Scissor = (x, y, width, height);
    }

    public void DrawArrays(PrimitiveKind primitive, uint first, uint count)
    {
        Record();
        Draws.Add(new DrawCall(primitive, count, 1, BoundVertexArray, _boundProgram, Indexed: false));
    }

    public void DrawElements(PrimitiveKind primitive, uint count)
    {
        Record();
        Draws.Add(new DrawCall(primitive, count, 1, BoundVertexArray, _boundProgram, Indexed: true));
    }

    public void DrawElementsInstanced(PrimitiveKind primitive, uint count, uint instanceCount)
    {
        Record();
        Draws.Add(new DrawCall(primitive, count, instanceCount, BoundVertexArray, _boundProgram, Indexed: true));
    }

    public void VertexAttribDivisor(uint index, uint divisor)
    {
        Record();
        Divisors[index] = divisor;
    }

    public BufferHandle CreateBuffer()
    {
        Record();
        uint id = _nextId++;
        LiveBuffers.Add(id);
        return new BufferHandle(id);
    }

    public void BindBuffer(BufferKind kind, BufferHandle handle)
    {
        Record();
        BoundBuffers[kind] = handle.Id;
    }

    public void BufferData<T>(BufferKind kind, ReadOnlySpan<T> data, BufferUsageKind usage) where T : unmanaged
    {
        Record();
        uint buffer = BoundBuffers[kind];
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);
        BufferStorage[buffer] = ((nuint)bytes.Length, usage);
        LastBufferWrite[buffer] = (0, bytes.ToArray());
    }

    public void BufferData(BufferKind kind, nuint sizeInBytes, BufferUsageKind usage)
    {
        Record();
        BufferStorage[BoundBuffers[kind]] = (sizeInBytes, usage);
    }

    public void BufferSubData<T>(BufferKind kind, nint offsetInBytes, ReadOnlySpan<T> data) where T : unmanaged
    {
        Record();
        LastBufferWrite[BoundBuffers[kind]] = (offsetInBytes, MemoryMarshal.AsBytes(data).ToArray());
    }

    public void DeleteBuffer(BufferHandle handle)
    {
        Record();
        LiveBuffers.Remove(handle.Id);
    }

    public void BindBufferBase(BufferKind kind, uint bindingPoint, BufferHandle handle) => Record();

    public uint GetUniformBlockIndex(ProgramHandle program, string blockName)
    {
        Record();
        return 0;
    }

    public void UniformBlockBinding(ProgramHandle program, uint blockIndex, uint bindingPoint) => Record();

    public VertexArrayHandle CreateVertexArray()
    {
        Record();
        uint id = _nextId++;
        LiveVertexArrays.Add(id);
        return new VertexArrayHandle(id);
    }

    public void BindVertexArray(VertexArrayHandle handle)
    {
        Record();
        BoundVertexArray = handle.Id;
    }

    public void EnableVertexAttribArray(uint index) => Record();

    public void VertexAttribPointer(uint index, int size, VertexAttribType type, bool normalized, uint stride, nint offset)
    {
        Record();
        Attributes.Add(new VertexAttribute(BoundVertexArray, index, size, type, stride, offset));
    }

    public void DeleteVertexArray(VertexArrayHandle handle)
    {
        Record();
        LiveVertexArrays.Remove(handle.Id);
    }

    public string PreprocessShaderSource(ShaderStage stage, string source)
    {
        Record();
        return PreprocessPrefix + source;
    }

    public ShaderHandle CreateShader(ShaderStage stage)
    {
        Record();
        uint id = _nextId++;
        LiveShaders.Add(id);
        ShaderSources[id] = (stage, "");
        return new ShaderHandle(id);
    }

    public void ShaderSource(ShaderHandle shader, string source)
    {
        Record();
        ShaderSources[shader.Id] = (ShaderSources[shader.Id].Stage, source);
    }

    public void CompileShader(ShaderHandle shader) => Record();

    public bool GetShaderCompileStatus(ShaderHandle shader)
    {
        Record();
        return FailCompileStage != ShaderSources[shader.Id].Stage;
    }

    public string GetShaderInfoLog(ShaderHandle shader)
    {
        Record();
        return $"fake compile error in {ShaderSources[shader.Id].Stage}";
    }

    public ProgramHandle CreateProgram()
    {
        Record();
        uint id = _nextId++;
        LivePrograms.Add(id);
        return new ProgramHandle(id);
    }

    public void AttachShader(ProgramHandle program, ShaderHandle shader) => Record();

    public void LinkProgram(ProgramHandle program) => Record();

    public bool GetProgramLinkStatus(ProgramHandle program)
    {
        Record();
        return !FailLink;
    }

    public string GetProgramInfoLog(ProgramHandle program)
    {
        Record();
        return "fake link error";
    }

    public void DetachShader(ProgramHandle program, ShaderHandle shader) => Record();

    public void DeleteShader(ShaderHandle shader)
    {
        Record();
        LiveShaders.Remove(shader.Id);
    }

    public void DeleteProgram(ProgramHandle program)
    {
        Record();
        LivePrograms.Remove(program.Id);
    }

    public void UseProgram(ProgramHandle program)
    {
        Record();
        _boundProgram = program.Id;
    }

    public UniformLocation GetUniformLocation(ProgramHandle program, string name)
    {
        Record();
        if (!_uniformLocations.TryGetValue((program.Id, name), out int location))
        {
            location = _uniformLocations.Count;
            _uniformLocations[(program.Id, name)] = location;
            _uniformNames[location] = name;
        }

        return new UniformLocation(location);
    }

    public void SetUniform(UniformLocation location, int value) => RecordUniform(location, value);

    public void SetUniform(UniformLocation location, float value) => RecordUniform(location, value);

    public void SetUniform(UniformLocation location, Vector2 value) => RecordUniform(location, value);

    public void SetUniform(UniformLocation location, Vector3 value) => RecordUniform(location, value);

    public void SetUniform(UniformLocation location, Vector4 value) => RecordUniform(location, value);

    public void SetUniformMatrix4(UniformLocation location, ReadOnlySpan<Matrix4x4> values) =>
        RecordUniform(location, values.Length == 1 ? values[0] : values.ToArray());

    public TextureHandle CreateTexture()
    {
        Record();
        uint id = _nextId++;
        LiveTextures.Add(id);
        return new TextureHandle(id);
    }

    public void BindTexture(uint unit, TextureHandle handle)
    {
        Record();
        BoundTextures[unit] = handle.Id;
    }

    public void SetTextureWrap(TextureWrap wrap)
    {
        Record();
        TextureWraps[BoundTextures[0]] = wrap;
    }

    public void SetTextureFilter(TextureFilter minFilter, TextureFilter magFilter)
    {
        Record();
        TextureFilters[BoundTextures[0]] = (minFilter, magFilter);
    }

    public float GetMaxAnisotropy()
    {
        Record();
        return MaxAnisotropy;
    }

    public void SetTextureMaxAnisotropy(float anisotropy)
    {
        Record();
        TextureAnisotropy[BoundTextures[0]] = anisotropy;
    }

    public void TextureImage2DRgba8(uint width, uint height, ReadOnlySpan<byte> rgba)
    {
        Record();
        TextureImages[BoundTextures[0]] = new TextureImage(TextureInternalFormat.Rgba8, width, height, rgba.ToArray());
    }

    public void TextureImage2D(TextureInternalFormat format, uint width, uint height, ReadOnlySpan<byte> data)
    {
        Record();
        TextureImages[BoundTextures[0]] = new TextureImage(format, width, height, data.ToArray());
    }

    public void SetTextureCompareMode(bool enabled) => Record();

    public void GenerateMipmap2D()
    {
        Record();
        MipmappedTextures.Add(BoundTextures[0]);
    }

    public void DeleteTexture(TextureHandle handle)
    {
        Record();
        LiveTextures.Remove(handle.Id);
    }

    public FramebufferHandle CreateFramebuffer()
    {
        Record();
        uint id = _nextId++;
        LiveFramebuffers.Add(id);
        FramebufferAttachments[id] = new Dictionary<RenderTargetAttachment, uint>();
        return new FramebufferHandle(id);
    }

    public void BindFramebuffer(FramebufferHandle handle)
    {
        Record();
        BoundFramebuffer = handle.Id;
    }

    public void FramebufferTexture2D(RenderTargetAttachment attachment, TextureHandle texture)
    {
        Record();
        FramebufferAttachments[BoundFramebuffer][attachment] = texture.Id;
    }

    public bool CheckFramebufferComplete()
    {
        Record();
        return !FailFramebuffer;
    }

    public void SetColorBuffersNone() => Record();

    /// <summary>The arguments of the last depth blit.</summary>
    public (uint Source, uint Destination, uint Width, uint Height, int X, int Y, uint DestWidth, uint DestHeight)? LastBlit
    {
        get;
        private set;
    }

    public void BlitDepth(FramebufferHandle source, FramebufferHandle destination, uint width, uint height,
        int destX, int destY, uint destWidth, uint destHeight)
    {
        Record();
        LastBlit = (source.Id, destination.Id, width, height, destX, destY, destWidth, destHeight);
        BoundFramebuffer = destination.Id;
    }

    public void DeleteFramebuffer(FramebufferHandle handle)
    {
        Record();
        LiveFramebuffers.Remove(handle.Id);
    }

    public void SetWireframe(bool enabled) => Record();

    /// <summary>The number of times the named command was issued.</summary>
    public int Count(string method) => Calls.Count(c => c == method);

    private void RecordUniform(UniformLocation location, object value)
    {
        Record("SetUniform");
        string name = _uniformNames.TryGetValue(location.Location, out string? n) ? n : $"<{location.Location}>";
        UniformWrites.Add((name, value));
    }

    private void Record([System.Runtime.CompilerServices.CallerMemberName] string method = "") => Calls.Add(method);

    internal readonly record struct DrawCall(
        PrimitiveKind Primitive, uint Count, uint Instances, uint VertexArray, uint Program, bool Indexed);

    internal readonly record struct VertexAttribute(
        uint VertexArray, uint Index, int Size, VertexAttribType Type, uint Stride, nint Offset);

    internal sealed record TextureImage(TextureInternalFormat Format, uint Width, uint Height, byte[] Data);
}
