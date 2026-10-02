using System.Numerics;
using Spot.Engine.Tests.Fakes;
using Spot.Rendering;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the low-level <see cref="Renderer"/> facade and the immediate-mode <see cref="Renderer2D"/> batcher
/// against a recording device.
/// </summary>
public class RendererTests
{
    private static RecordingGraphicsDevice Install()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        return device;
    }

    private static RecordingGraphicsDevice Install2D()
    {
        RecordingGraphicsDevice device = Install();
        Renderer2D.Init();
        return device;
    }

    [Fact]
    public void Init_InstallsDeviceAndResetsTrackedState()
    {
        RecordingGraphicsDevice first = Install();
        Renderer.SetViewport(5, 6, 100, 50);

        RecordingGraphicsDevice second = Install();

        Assert.True(Renderer.IsInitialized);
        Assert.Same(second, Renderer.Device);
        Assert.NotSame(first, Renderer.Device);
        Assert.Equal(0u, Renderer.ViewportWidth);
        Assert.Equal(FramebufferHandle.Default, Renderer.CurrentRenderTarget);
    }

    [Fact]
    public void Init_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Renderer.Init((IGraphicsDevice)null!));
    }

    [Fact]
    public void SetViewport_ForwardsAndTracks()
    {
        RecordingGraphicsDevice device = Install();

        Renderer.SetViewport(10, 20, 640, 480);

        Assert.Equal((10, 20, 640u, 480u), device.Viewport);
        Assert.Equal((10, 20, 640u, 480u),
            (Renderer.ViewportX, Renderer.ViewportY, Renderer.ViewportWidth, Renderer.ViewportHeight));
    }

    [Fact]
    public void BindRenderTarget_BindsFramebufferAndTracksIt()
    {
        RecordingGraphicsDevice device = Install();
        var target = new FramebufferHandle(42);

        Renderer.BindRenderTarget(target, 0, 0, 256, 128);

        Assert.Equal(42u, device.BoundFramebuffer);
        Assert.Equal(target, Renderer.CurrentRenderTarget);
        Assert.Equal((0, 0, 256u, 128u), device.Viewport);
    }

    [Fact]
    public void ClearAndStateHelpers_ForwardToDevice()
    {
        RecordingGraphicsDevice device = Install();

        Renderer.SetClearColor(0.1f, 0.2f, 0.3f, 1.0f);
        Renderer.Clear();
        Assert.Equal((true, true), device.LastClear);

        Renderer.ClearDepth();
        Assert.Equal((false, true), device.LastClear);

        Renderer.SetDepthTest(true);
        Renderer.SetFaceCulling(false);
        Renderer.SetDepthWrite(false);
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 1.0f), device.ClearColor);
        Assert.True(device.Capabilities[GraphicsCapability.DepthTest]);
        Assert.False(device.Capabilities[GraphicsCapability.CullFace]);
        Assert.False(device.DepthWrite);
    }

    [Fact]
    public void DrawCalls_BindTheVertexArrayAndUseItsIndexCount()
    {
        RecordingGraphicsDevice device = Install();
        using var vao = new VertexArray();
        using var ibo = new IndexBuffer(new uint[] { 0, 1, 2 });
        vao.SetIndexBuffer(ibo);

        Renderer.DrawIndexed(vao);
        Renderer.DrawArrays(vao, 4);
        Renderer.DrawIndexedInstanced(vao, 3, 10);

        Assert.Equal(
            new[]
            {
                new RecordingGraphicsDevice.DrawCall(PrimitiveKind.Triangles, 3, 1, vao.Handle.Id, 0, true),
                new RecordingGraphicsDevice.DrawCall(PrimitiveKind.Triangles, 4, 1, vao.Handle.Id, 0, false),
                new RecordingGraphicsDevice.DrawCall(PrimitiveKind.Triangles, 3, 10, vao.Handle.Id, 0, true),
            },
            device.Draws);
    }

    [Fact]
    public void Renderer2D_QuadsSharingATextureBatchIntoOneDraw()
    {
        RecordingGraphicsDevice device = Install2D();

        Renderer2D.BeginScene(Matrix4x4.Identity);
        for (int i = 0; i < 3; i++)
        {
            Renderer2D.DrawQuad(new Vector2(i, 0), Vector2.One, Vector4.One);
        }

        Renderer2D.EndScene();

        RecordingGraphicsDevice.DrawCall draw = Assert.Single(device.Draws);
        Assert.Equal(18u, draw.Count);
        Assert.True(draw.Indexed);
    }

    [Fact]
    public void Renderer2D_TextureChangeFlushesTheBatch()
    {
        RecordingGraphicsDevice device = Install2D();
        using var a = new Texture2D(1, 1, new byte[4]);
        using var b = new Texture2D(1, 1, new byte[4]);

        Renderer2D.BeginScene(Matrix4x4.Identity);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, a);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, a);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, b);
        Renderer2D.EndScene();

        Assert.Equal(new uint[] { 12, 6 }, device.Draws.Select(d => d.Count));
    }

    [Fact]
    public void Renderer2D_EmptySceneIssuesNoDraw()
    {
        RecordingGraphicsDevice device = Install2D();

        Renderer2D.BeginScene(Matrix4x4.Identity);
        Renderer2D.EndScene();

        Assert.Empty(device.Draws);
    }

    [Fact]
    public void Renderer2D_FullBatchFlushesAndContinues()
    {
        RecordingGraphicsDevice device = Install2D();

        Renderer2D.BeginScene(Matrix4x4.Identity);
        for (int i = 0; i < 10_001; i++)
        {
            Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        }

        Renderer2D.EndScene();

        Assert.Equal(new uint[] { 60_000, 6 }, device.Draws.Select(d => d.Count));
    }

    [Fact]
    public void Renderer2D_UploadsTransformedCornersAndColorAndTheViewProjection()
    {
        RecordingGraphicsDevice device = Install2D();
        Matrix4x4 viewProjection = Matrix4x4.CreateOrthographicOffCenter(0, 800, 600, 0, -1, 1);
        var color = new Vector4(0.25f, 0.5f, 0.75f, 1.0f);

        Renderer2D.BeginScene(viewProjection);
        Renderer2D.DrawQuad(new Vector2(100, 50), new Vector2(20, 10), color);
        Renderer2D.EndScene();

        float[] vertices = device.BufferContents<float>(device.BoundBuffers[BufferKind.Vertex]);
        // Layout: position (3), color (4), uv (2). The first corner is the quad's bottom-left.
        Assert.Equal(new[] { 90f, 45f, 0f, 0.25f, 0.5f, 0.75f, 1f, 0f, 0f }, vertices[..9]);
        Assert.Equal(new[] { 110f, 55f }, vertices[18..20]); // third corner: top-right
        Assert.Equal(viewProjection, device.Uniform("uViewProjection"));
        Assert.Equal(0, device.Uniform("uTexture"));
    }

    [Fact]
    public void Renderer2D_DrawRectEmitsFourEdgesAndLinesTwoQuads()
    {
        RecordingGraphicsDevice device = Install2D();

        Renderer2D.BeginScene(Matrix4x4.Identity);
        Renderer2D.DrawRect(Vector2.Zero, new Vector2(4, 2), Vector4.One);
        Renderer2D.DrawLine(Vector3.Zero, new Vector3(1, 1, 0), Vector4.One);
        Renderer2D.DrawLine(Vector3.One, Vector3.One, Vector4.One); // zero length: skipped
        Renderer2D.EndScene();

        Assert.Equal(6u * 6u, Assert.Single(device.Draws).Count);
    }

    [Fact]
    public void Renderer2D_InitializesLazilyOnFirstUse()
    {
        Renderer2D.Shutdown();
        RecordingGraphicsDevice device = Install();

        Renderer2D.BeginScene(Matrix4x4.Identity);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        Renderer2D.EndScene();

        Assert.Equal(6u, Assert.Single(device.Draws).Count);
    }

    [Fact]
    public void Renderer2D_RecreatesItsResourcesOnANewDevice()
    {
        RecordingGraphicsDevice first = Install2D();
        Renderer2D.BeginScene(Matrix4x4.Identity);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        Renderer2D.EndScene();

        RecordingGraphicsDevice second = Install();
        Renderer2D.BeginScene(Matrix4x4.Identity);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        Renderer2D.EndScene();

        Assert.Single(first.Draws);
        Assert.Single(second.Draws);
        Assert.NotEmpty(second.LivePrograms);
    }

    [Fact]
    public void Renderer2D_FlushSubmitsAndStartsAFreshBatch()
    {
        RecordingGraphicsDevice device = Install2D();

        Renderer2D.BeginScene(Matrix4x4.Identity);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        Renderer2D.Flush();
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        Renderer2D.EndScene();
        Renderer2D.EndScene(); // nothing left: no extra draw

        Assert.Equal(new uint[] { 12, 6 }, device.Draws.Select(d => d.Count));
    }

    [Fact]
    public void Renderer2D_ShutdownReleasesItsResources()
    {
        RecordingGraphicsDevice device = Install2D();
        Assert.NotEmpty(device.LivePrograms);

        Renderer2D.Shutdown();

        Assert.Empty(device.LivePrograms);
        Assert.Empty(device.LiveTextures);
        Assert.Empty(device.LiveVertexArrays);
        Assert.Empty(device.LiveBuffers);
    }

    [Fact]
    public void Renderer2D_ExposesTheBatchViewProjection()
    {
        Install2D();
        Matrix4x4 viewProjection = Matrix4x4.CreateScale(2f);

        Renderer2D.BeginScene(viewProjection);

        Assert.Equal(viewProjection, Renderer2D.ViewProjection);
        Renderer2D.EndScene();
    }

    [Fact]
    public void EditorGrid_FlushesPendingQuadsThenDrawsAFullScreenTriangle()
    {
        RecordingGraphicsDevice device = Install2D();
        Matrix4x4 viewProjection = Matrix4x4.CreateScale(0.5f);

        Renderer2D.BeginScene(viewProjection);
        Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
        EditorGrid.Draw2D(zoom: 3f);
        Renderer2D.EndScene();

        Assert.Equal(new[] { (6u, true), (3u, false) }, device.Draws.Select(d => (d.Count, d.Indexed)));
        Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse);
        Assert.Equal(inverse, device.Uniform("uInverseViewProjection"));
        Assert.Equal(3f, device.Uniform("uZoom"));
        Assert.True(device.Capabilities[GraphicsCapability.Blend]);
    }
}
