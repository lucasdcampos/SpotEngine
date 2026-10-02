using System.Numerics;
using Spot.Engine.Tests.Fakes;
using Spot.Rendering;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the low-level GPU wrappers (buffers, vertex arrays, shaders, textures) against a recording device,
/// so the exact commands they issue are pinned without a real GL context.
/// </summary>
public class GpuResourceTests
{
    private static RecordingGraphicsDevice Install()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        return device;
    }

    [Fact]
    public void StaticBuffer_UploadsDataOnceAsStaticDraw()
    {
        RecordingGraphicsDevice device = Install();

        using var buffer = new GraphicsBuffer<float>(new float[] { 1, 2, 3 }, BufferKind.Vertex);

        Assert.Contains(buffer.Handle.Id, device.LiveBuffers);
        Assert.Equal(BufferKind.Vertex, buffer.Kind);
        Assert.Equal(3u, buffer.Capacity);
        Assert.Equal(((nuint)12, BufferUsageKind.StaticDraw), device.BufferStorage[buffer.Handle.Id]);
        Assert.Equal(new float[] { 1, 2, 3 }, device.BufferContents<float>(buffer.Handle.Id));
    }

    [Fact]
    public void DynamicBuffer_AllocatesCapacityAndWritesAtElementOffset()
    {
        RecordingGraphicsDevice device = Install();

        using var buffer = new GraphicsBuffer<uint>(8, BufferKind.Index);
        buffer.SetData(2, new uint[] { 7, 9 });

        Assert.Equal(((nuint)32, BufferUsageKind.DynamicDraw), device.BufferStorage[buffer.Handle.Id]);
        Assert.Equal((nint)8, device.LastBufferWrite[buffer.Handle.Id].Offset);
        Assert.Equal(new uint[] { 7, 9 }, device.BufferContents<uint>(buffer.Handle.Id));
    }

    [Fact]
    public void Buffer_DisposeDeletesOnceAndIsIdempotent()
    {
        RecordingGraphicsDevice device = Install();
        var buffer = new GraphicsBuffer<float>(4, BufferKind.Uniform);
        uint id = buffer.Handle.Id;

        buffer.Dispose();
        buffer.Dispose();

        Assert.DoesNotContain(id, device.LiveBuffers);
        Assert.Equal(1, device.Count(nameof(IGraphicsDevice.DeleteBuffer)));
    }

    [Fact]
    public void VertexBuffer_ExposesLayoutStrideAndBuffer()
    {
        Install();

        using var vbo = new VertexBuffer(16, ShaderDataType.Float3, ShaderDataType.Float4, ShaderDataType.Float2);

        Assert.Equal(new[] { ShaderDataType.Float3, ShaderDataType.Float4, ShaderDataType.Float2 }, vbo.Layout);
        Assert.Equal(36u, vbo.Stride);
        Assert.Equal(16u, vbo.Buffer.Capacity);
    }

    [Fact]
    public void VertexArray_ConfiguresAttributesInLayoutOrderWithTightOffsets()
    {
        RecordingGraphicsDevice device = Install();
        using var vao = new VertexArray();
        using var vbo = new VertexBuffer(9, ShaderDataType.Float3, ShaderDataType.Float4, ShaderDataType.Float2);

        vao.AddVertexBuffer(vbo);

        Assert.Equal(3u, vao.AttributeCount);
        Assert.Equal(
            new[]
            {
                new RecordingGraphicsDevice.VertexAttribute(vao.Handle.Id, 0, 3, VertexAttribType.Float, 36, 0),
                new RecordingGraphicsDevice.VertexAttribute(vao.Handle.Id, 1, 4, VertexAttribType.Float, 36, 12),
                new RecordingGraphicsDevice.VertexAttribute(vao.Handle.Id, 2, 2, VertexAttribType.Float, 36, 28),
            },
            device.Attributes);
        Assert.Empty(device.Divisors);
    }

    [Fact]
    public void VertexArray_InstancedBufferContinuesAttributeIndicesWithDivisors()
    {
        RecordingGraphicsDevice device = Install();
        using var vao = new VertexArray();
        using var geometry = new VertexBuffer(3, ShaderDataType.Float3);
        using var instances = new VertexBuffer(8, ShaderDataType.Float4, ShaderDataType.Float4);

        vao.AddVertexBuffer(geometry);
        vao.AddInstancedVertexBuffer(instances, divisor: 2);

        Assert.Equal(3u, vao.AttributeCount);
        Assert.Equal(new Dictionary<uint, uint> { [1] = 2, [2] = 2 }, device.Divisors);
        Assert.Equal((nint)16, device.Attributes[2].Offset);
    }

    [Fact]
    public void VertexArray_IndexCountTracksAttachedIndexBuffer()
    {
        Install();
        using var vao = new VertexArray();
        Assert.Equal(0u, vao.IndexCount);
        Assert.Null(vao.IndexBuffer);

        using var ibo = new IndexBuffer(new uint[] { 0, 1, 2, 2, 3, 0 });
        vao.SetIndexBuffer(ibo);

        Assert.Equal(6u, vao.IndexCount);
        Assert.Same(ibo, vao.IndexBuffer);
    }

    [Fact]
    public void Shader_ValidProgram_CleansUpStagesAndPreprocessesSources()
    {
        RecordingGraphicsDevice device = Install();
        device.PreprocessPrefix = "//pre\n";

        using var shader = new Shader("vertex-src", "fragment-src");

        Assert.True(shader.IsValid);
        Assert.Null(shader.ErrorLog);
        Assert.Contains(shader.Handle.Id, device.LivePrograms);
        Assert.Empty(device.LiveShaders); // stages are deleted once linked
        Assert.Contains(device.ShaderSources.Values, s => s == (ShaderStage.Vertex, "//pre\nvertex-src"));
        Assert.Contains(device.ShaderSources.Values, s => s == (ShaderStage.Fragment, "//pre\nfragment-src"));
    }

    [Fact]
    public void Shader_CompileFailure_IsReportedWithoutThrowing()
    {
        RecordingGraphicsDevice device = Install();
        device.FailCompileStage = ShaderStage.Fragment;

        using var shader = new Shader("v", "f");

        Assert.False(shader.IsValid);
        Assert.Contains("Fragment", shader.ErrorLog);
        Assert.Contains(shader.Handle.Id, device.LivePrograms);
    }

    [Fact]
    public void Shader_LinkFailure_IsReported()
    {
        RecordingGraphicsDevice device = Install();
        device.FailLink = true;

        using var shader = new Shader("v", "f");

        Assert.False(shader.IsValid);
        Assert.Contains("fake link error", shader.ErrorLog);
    }

    [Fact]
    public void Shader_UniformLocationIsLookedUpOncePerName()
    {
        RecordingGraphicsDevice device = Install();
        using var shader = new Shader("v", "f");

        shader.SetUniform("uTint", new Vector4(1, 2, 3, 4));
        shader.SetUniform("uTint", Vector4.One);
        shader.SetUniform("uTime", 0.5f);

        Assert.Equal(2, device.Count(nameof(IGraphicsDevice.GetUniformLocation)));
        Assert.Equal(Vector4.One, device.Uniform("uTint"));
        Assert.Equal(0.5f, device.Uniform("uTime"));
    }

    [Fact]
    public void Shader_EmptyMatrixArrayIsIgnored()
    {
        RecordingGraphicsDevice device = Install();
        using var shader = new Shader("v", "f");

        shader.SetUniform("uBones", ReadOnlySpan<Matrix4x4>.Empty);

        Assert.Equal(0, device.Count(nameof(IGraphicsDevice.SetUniformMatrix4)));
    }

    [Fact]
    public void Texture_FromPixels_UploadsRgbaWithMipmapsAndCappedAnisotropy()
    {
        RecordingGraphicsDevice device = Install();
        byte[] pixels = { 1, 2, 3, 4, 5, 6, 7, 8 };

        using var texture = new Texture2D(2, 1, pixels);

        uint id = texture.Handle.Id;
        Assert.Equal(2u, texture.Width);
        Assert.Equal(1u, texture.Height);
        Assert.Equal(TextureInternalFormat.Rgba8, device.TextureImages[id].Format);
        Assert.Equal(pixels, device.TextureImages[id].Data);
        Assert.Equal((TextureFilter.LinearMipmapLinear, TextureFilter.Linear), device.TextureFilters[id]);
        Assert.Equal(TextureWrap.Repeat, device.TextureWraps[id]);
        Assert.Contains(id, device.MipmappedTextures);
        Assert.Equal(8.0f, device.TextureAnisotropy[id]);
    }

    [Fact]
    public void Texture_PointFilterUsesNearestAndSkipsAnisotropyWhenUnsupported()
    {
        RecordingGraphicsDevice device = Install();
        device.MaxAnisotropy = 1.0f;

        using var texture = new Texture2D(1, 1, new byte[] { 255, 255, 255, 255 }, pointFilter: true);

        Assert.Equal((TextureFilter.Nearest, TextureFilter.Nearest), device.TextureFilters[texture.Handle.Id]);
        Assert.False(device.TextureAnisotropy.ContainsKey(texture.Handle.Id));
    }

    [Fact]
    public void Texture_BindAndDisposeOnce()
    {
        RecordingGraphicsDevice device = Install();
        var texture = new Texture2D(1, 1, new byte[4]);

        texture.Bind(3);
        Assert.Equal(texture.Handle.Id, device.BoundTextures[3]);

        uint id = texture.Handle.Id;
        texture.Dispose();
        texture.Dispose();
        Assert.DoesNotContain(id, device.LiveTextures);
        Assert.Equal(1, device.Count(nameof(IGraphicsDevice.DeleteTexture)));
    }

    [Theory]
    [InlineData(ShaderDataType.Float, 4u, 1, VertexAttribType.Float)]
    [InlineData(ShaderDataType.Float4, 16u, 4, VertexAttribType.Float)]
    [InlineData(ShaderDataType.Int3, 12u, 3, VertexAttribType.Int)]
    [InlineData(ShaderDataType.Bool, 1u, 1, VertexAttribType.UnsignedByte)]
    public void ShaderDataType_MapsSizeComponentsAndApiType(
        ShaderDataType type, uint size, int components, VertexAttribType apiType)
    {
        Assert.Equal(size, type.Size());
        Assert.Equal(components, type.ComponentCount());
        Assert.Equal(apiType, type.ToVertexAttribType());
    }

    [Fact]
    public void ShaderDataType_NoneIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShaderDataType.None.Size());
    }
}
