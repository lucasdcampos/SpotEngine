using System.Numerics;
using Spot.Engine.Graphics;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the blended billboard batch and the fullscreen pass against a recording device.
/// </summary>
public class BillboardAndFullscreenTests
{
    private const int Stride = 9;

    private static RecordingGraphicsDevice Install()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        return device;
    }

    private static float[] Vertices(RecordingGraphicsDevice device) =>
        device.BufferContents<float>(device.BoundBuffers[BufferKind.Vertex]);

    private static Vector2 Uv(float[] v, int vertex) => new(v[vertex * Stride + 7], v[vertex * Stride + 8]);

    [Fact]
    public void Draw_SpansCenterPlusMinusItsAxes()
    {
        RecordingGraphicsDevice device = Install();

        BillboardBatch.Begin(Matrix4x4.Identity);
        BillboardBatch.Draw(new Vector3(1, 1, 1), new Vector3(2, 0, 0), new Vector3(0, 3, 0), Vector4.One);
        BillboardBatch.End();

        float[] v = Vertices(device);
        Assert.Equal(new[] { -1f, -2f, 1f }, v[..3]);              // center - x - y
        Assert.Equal(new[] { 3f, 4f, 1f }, v[(2 * Stride)..(2 * Stride + 3)]); // center + x + y
        Assert.Equal(new Vector2(0, 0), Uv(v, 0));
        Assert.Equal(new Vector2(1, 1), Uv(v, 2));
        Assert.Equal(BillboardBatch.SoftDotTexture.Handle.Id, device.BoundTextures[0]);
    }

    [Fact]
    public void Draw_AnAtlasRectPutsItsTopOnThePositiveYEdge()
    {
        RecordingGraphicsDevice device = Install();
        using var atlas = new Texture2D(1, 1, new byte[4]);

        BillboardBatch.Begin(Matrix4x4.Identity);
        BillboardBatch.Draw(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector4.One, atlas, uv: new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
        BillboardBatch.End();

        float[] v = Vertices(device);
        Assert.Equal(new Vector2(0.1f, 0.4f), Uv(v, 0)); // bottom-left samples the rect's bottom (v1)
        Assert.Equal(new Vector2(0.3f, 0.2f), Uv(v, 2)); // top-right samples its top (v0)
    }

    [Fact]
    public void Batch_BreaksOnTextureAndBlendChanges()
    {
        RecordingGraphicsDevice device = Install();
        using var other = new Texture2D(1, 1, new byte[4]);

        BillboardBatch.Begin(Matrix4x4.Identity);
        BillboardBatch.Draw(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector4.One);
        BillboardBatch.Draw(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector4.One);
        BillboardBatch.Draw(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector4.One, blend: BlendMode.Additive);
        BillboardBatch.Draw(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector4.One, other, BlendMode.Additive);
        BillboardBatch.End();

        Assert.Equal(new uint[] { 12, 6, 6 }, device.Draws.Select(d => d.Count));
        Assert.Equal((BlendFactor.SrcAlpha, BlendFactor.One), device.BlendFunc); // the last batch was additive
    }

    [Fact]
    public void End_RestoresDepthWritesAndOpaqueDrawing()
    {
        RecordingGraphicsDevice device = Install();

        BillboardBatch.Begin(Matrix4x4.Identity);
        BillboardBatch.Draw(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector4.One);
        BillboardBatch.End();

        Assert.True(device.DepthWrite);
        Assert.False(device.Capabilities[GraphicsCapability.Blend]);
        Assert.Contains(nameof(IGraphicsDevice.SetDepthWrite), device.Calls);
    }

    [Fact]
    public void DrawFacing_UsesHalfTheSizeAlongTheCameraAxes()
    {
        RecordingGraphicsDevice device = Install();
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        BillboardBatch.CameraAxes(view, out Vector3 right, out Vector3 up);

        BillboardBatch.Begin(Matrix4x4.Identity);
        BillboardBatch.DrawFacing(Vector3.Zero, new Vector2(4, 2), right, up, Vector4.One);
        BillboardBatch.End();

        Assert.Equal(Vector3.UnitX, right);
        Assert.Equal(Vector3.UnitY, up);
        Assert.Equal(new[] { -2f, -1f, 0f }, Vertices(device)[..3]);
    }

    [Fact]
    public void Fullscreen_DrawsThreeVerticesWithTheGivenShader()
    {
        RecordingGraphicsDevice device = Install();
        using Shader shader = FullscreenPass.CreateShader("fragment");

        FullscreenPass.Draw(shader);

        RecordingGraphicsDevice.DrawCall draw = Assert.Single(device.Draws);
        Assert.Equal((3u, false, shader.Handle.Id), (draw.Count, draw.Indexed, draw.Program));
        Assert.Contains(device.ShaderSources.Values, s => s.Stage == ShaderStage.Vertex && s.Source.Contains("gl_VertexID"));
    }

    [Fact]
    public void Fullscreen_BindsTheSourceTextureAsUSource()
    {
        RecordingGraphicsDevice device = Install();
        using Shader shader = FullscreenPass.CreateShader("fragment");
        using var source = new Texture2D(1, 1, new byte[4]);

        FullscreenPass.Draw(shader, source.Handle);

        Assert.Equal(source.Handle.Id, device.BoundTextures[0]);
        Assert.Equal(0, device.Uniform("uSource"));
        Assert.Single(device.Draws);
    }
}
