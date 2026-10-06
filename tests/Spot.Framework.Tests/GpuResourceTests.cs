using System.Numerics;
using Spot.Tests.Fakes;
using Spot.Engine.Graphics;

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

    [Theory]
    [InlineData(FramebufferFormat.RGBA8, TextureInternalFormat.Rgba8)]
    [InlineData(FramebufferFormat.RGBA16F, TextureInternalFormat.Rgba16F)]
    public void Framebuffer_CreatesColorAndDepthStencilAttachments(FramebufferFormat format, TextureInternalFormat color)
    {
        RecordingGraphicsDevice device = Install();

        using var fb = new Framebuffer(64, 32, format);

        Dictionary<RenderTargetAttachment, uint> attachments = device.FramebufferAttachments[fb.Handle.Id];
        Assert.Equal(fb.ColorTexture.Id, attachments[RenderTargetAttachment.Color0]);
        Assert.Equal(fb.DepthTexture.Id, attachments[RenderTargetAttachment.DepthStencil]);
        Assert.Equal(fb.ColorTexture.Id, fb.ColorAttachment);
        Assert.Equal(color, device.TextureImages[fb.ColorTexture.Id].Format);
        Assert.Equal(TextureInternalFormat.Depth24Stencil8, device.TextureImages[fb.DepthTexture.Id].Format);
        Assert.Equal((64u, 32u), (device.TextureImages[fb.DepthTexture.Id].Width, device.TextureImages[fb.DepthTexture.Id].Height));
    }

    [Fact]
    public void Framebuffer_CreationLeavesThePreviouslyBoundTargetBound()
    {
        RecordingGraphicsDevice device = Install();
        using var outer = new Framebuffer(8, 8);
        outer.Bind();

        using var inner = new Framebuffer(4, 4);

        Assert.Equal(outer.Handle.Id, device.BoundFramebuffer);
        Assert.Equal(outer.Handle, Renderer.CurrentRenderTarget);
    }

    [Fact]
    public void Framebuffer_BindTracksTheTargetAndUnbindRestoresTheScreen()
    {
        RecordingGraphicsDevice device = Install();
        using var fb = new Framebuffer(128, 64);

        fb.Bind();
        Assert.Equal(fb.Handle, Renderer.CurrentRenderTarget);
        Assert.Equal((0, 0, 128u, 64u), device.Viewport);

        fb.Unbind();
        Assert.Equal(FramebufferHandle.Default, Renderer.CurrentRenderTarget);
        Assert.Equal(0u, device.BoundFramebuffer);
        Assert.Equal((0, 0, 128u, 64u), device.Viewport); // the viewport is left alone
    }

    [Fact]
    public void Framebuffer_IncompleteTargetThrowsWithoutLeaking()
    {
        RecordingGraphicsDevice device = Install();
        device.FailFramebuffer = true;

        Assert.Throws<InvalidOperationException>(() => new Framebuffer(16, 16));

        Assert.Empty(device.LiveFramebuffers);
        Assert.Empty(device.LiveTextures);
    }

    [Fact]
    public void Framebuffer_ResizeRecreatesOnlyOnARealChange()
    {
        RecordingGraphicsDevice device = Install();
        using var fb = new Framebuffer(16, 16);
        uint first = fb.Handle.Id;

        fb.Resize(16, 16);
        fb.Resize(0, 32);
        Assert.Equal(first, fb.Handle.Id);

        fb.Resize(32, 8);
        Assert.NotEqual(first, fb.Handle.Id);
        Assert.Equal((32u, 8u), (fb.Width, fb.Height));
        Assert.DoesNotContain(first, device.LiveFramebuffers);
        Assert.Single(device.LiveFramebuffers);
        Assert.Equal(2, device.LiveTextures.Count);
    }

    [Fact]
    public void Framebuffer_DisposeReleasesEverythingOnce()
    {
        RecordingGraphicsDevice device = Install();
        var fb = new Framebuffer(16, 16);

        fb.Dispose();
        fb.Dispose();

        Assert.Empty(device.LiveFramebuffers);
        Assert.Empty(device.LiveTextures);
        Assert.Equal(1, device.Count(nameof(IGraphicsDevice.DeleteFramebuffer)));
    }

    [Fact]
    public void Framebuffer_BlitDepthToCopiesItsWholeDepthIntoTheRegion()
    {
        RecordingGraphicsDevice device = Install();
        using var fb = new Framebuffer(100, 50);

        fb.BlitDepthTo(7, 10, 20, 200, 100);

        Assert.Equal((fb.Handle.Id, 7u, 100u, 50u, 10, 20, 200u, 100u), device.LastBlit);
        Assert.Equal(7u, device.BoundFramebuffer);
    }
}
