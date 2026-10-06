using System.Numerics;
using Spot.Engine.Graphics;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers Renderer2D's quad-from-corners primitive and the framework's shapes and sprites built on it, by reading
/// back the vertices the batch uploads (layout: position 3, color 4, uv 2).
/// </summary>
public class Shapes2DTests
{
    private const int Stride = 9;

    private static RecordingGraphicsDevice Install()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        Renderer2D.Init();
        return device;
    }

    // Draws inside a scene and returns the uploaded vertex floats.
    private static float[] Draw(RecordingGraphicsDevice device, Action draw)
    {
        Renderer2D.BeginScene(Matrix4x4.Identity);
        draw();
        Renderer2D.EndScene();
        return device.BufferContents<float>(device.BoundBuffers[BufferKind.Vertex]);
    }

    private static Vector3 Position(float[] v, int vertex) =>
        new(v[vertex * Stride], v[vertex * Stride + 1], v[vertex * Stride + 2]);

    private static Vector2 Uv(float[] v, int vertex) => new(v[vertex * Stride + 7], v[vertex * Stride + 8]);

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Fact]
    public void CornerQuad_UploadsTheCornersAndTheUvRectInOrder()
    {
        RecordingGraphicsDevice device = Install();
        using var texture = new Texture2D(1, 1, new byte[4]);

        float[] v = Draw(device, () => Renderer2D.DrawQuad(
            new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 1, 0), new Vector3(0, 1, 0),
            Vector4.One, texture, new Vector4(0.25f, 0.5f, 0.75f, 1.0f)));

        Assert.Equal(new Vector3(2, 1, 0), Position(v, 2));
        Assert.Equal(new Vector2(0.25f, 0.5f), Uv(v, 0));
        Assert.Equal(new Vector2(0.75f, 0.5f), Uv(v, 1));
        Assert.Equal(new Vector2(0.75f, 1.0f), Uv(v, 2));
        Assert.Equal(new Vector2(0.25f, 1.0f), Uv(v, 3));
        Assert.Equal(texture.Handle.Id, device.BoundTextures[0]);
    }

    [Fact]
    public void CornerQuad_WithoutATextureBatchesWithColoredQuads()
    {
        RecordingGraphicsDevice device = Install();

        Draw(device, () =>
        {
            Renderer2D.DrawQuad(Vector2.Zero, Vector2.One, Vector4.One);
            Renderer2D.DrawQuad(Vector3.Zero, Vector3.UnitX, Vector3.One, Vector3.UnitY, Vector4.One);
        });

        Assert.Equal(12u, Assert.Single(device.Draws).Count);
        Assert.Equal(Renderer2D.WhiteTexture.Handle.Id, device.BoundTextures[0]);
    }

    [Fact]
    public void Triangle_IsADegenerateQuadRepeatingTheLastCorner()
    {
        RecordingGraphicsDevice device = Install();

        float[] v = Draw(device, () => Renderer2D.DrawTriangle(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), Vector4.One));

        Assert.Equal(new Vector3(0, 1, 0), Position(v, 2));
        Assert.Equal(Position(v, 2), Position(v, 3));
        Assert.Equal(6u, Assert.Single(device.Draws).Count);
    }

    [Theory]
    [InlineData(32, 32)]
    [InlineData(8, 8)]
    [InlineData(1, Shapes2D.MinCircleSegments)]
    public void Circle_IsAFanOfSegments(int requested, int expected)
    {
        RecordingGraphicsDevice device = Install();

        float[] v = Draw(device, () => Renderer2D.DrawCircle(new Vector2(5, 5), 2.0f, Vector4.One, requested));

        Assert.Equal((uint)(expected * 6), Assert.Single(device.Draws).Count);
        for (int segment = 0; segment < expected; segment++)
        {
            AssertNear(new Vector3(5, 5, 0), Position(v, segment * 4));
            Assert.Equal(2.0f, Vector3.Distance(new Vector3(5, 5, 0), Position(v, segment * 4 + 1)), 3);
        }
    }

    [Fact]
    public void Circle_WithNoRadiusDrawsNothing()
    {
        RecordingGraphicsDevice device = Install();

        Renderer2D.BeginScene(Matrix4x4.Identity);
        Renderer2D.DrawCircle(Vector2.Zero, 0.0f, Vector4.One);
        Renderer2D.EndScene();

        Assert.Empty(device.Draws);
    }

    [Fact]
    public void Polygon_IsAFanOfTrianglesAndIgnoresDegenerateInput()
    {
        RecordingGraphicsDevice device = Install();
        Vector2[] pentagon = { new(0, 0), new(2, 0), new(3, 1), new(1, 2), new(-1, 1) };

        Draw(device, () =>
        {
            Renderer2D.DrawPolygon(pentagon, Vector4.One);
            Renderer2D.DrawPolygon(pentagon.AsSpan(0, 2), Vector4.One);
        });

        Assert.Equal(3u * 6u, Assert.Single(device.Draws).Count);
    }

    [Fact]
    public void Sprite_CutsItsRegionFromTheAtlasWithATopLeftOrigin()
    {
        RecordingGraphicsDevice device = Install();
        using var atlas = new Texture2D(64, 32, new byte[64 * 32 * 4]);

        float[] v = Draw(device, () => Renderer2D.DrawSprite(atlas, new Vector2(10, 10), new Vector2(4, 2),
            sourceRect: new Vector4(16, 0, 16, 8)));

        // The top 8 rows of a 32-row texture are v in [0.75, 1] once stored bottom-up.
        Assert.Equal(new Vector2(0.25f, 0.75f), Uv(v, 0));
        Assert.Equal(new Vector2(0.5f, 1.0f), Uv(v, 2));
        AssertNear(new Vector3(8, 9, 0), Position(v, 0));
        AssertNear(new Vector3(12, 11, 0), Position(v, 2));
    }

    [Fact]
    public void Sprite_FlipsSwapTheUvEdges()
    {
        RecordingGraphicsDevice device = Install();
        using var texture = new Texture2D(1, 1, new byte[4]);

        float[] v = Draw(device, () => Renderer2D.DrawSprite(texture, Vector2.Zero, Vector2.One, flipX: true, flipY: true));

        Assert.Equal(new Vector2(1, 1), Uv(v, 0));
        Assert.Equal(new Vector2(0, 0), Uv(v, 2));
    }

    [Fact]
    public void Sprite_RotatesAroundItsCenter()
    {
        RecordingGraphicsDevice device = Install();
        using var texture = new Texture2D(1, 1, new byte[4]);

        float[] v = Draw(device, () => Renderer2D.DrawSprite(texture, new Vector2(1, 1), new Vector2(2, 2), rotation: MathF.PI / 2));

        // A quarter turn moves the bottom-left corner (0,0) to the bottom-right (2,0).
        AssertNear(new Vector3(2, 0, 0), Position(v, 0));
    }

    [Theory]
    [InlineData(0, 0, 64, 32, 0.0f, 0.0f, 1.0f, 1.0f)]
    [InlineData(0, 24, 32, 8, 0.0f, 0.0f, 0.5f, 0.25f)]
    public void SpriteUv_ConvertsTopLeftPixelsToBottomUpUvs(
        float x, float y, float w, float h, float u0, float v0, float u1, float v1)
    {
        Assert.Equal(new Vector4(u0, v0, u1, v1), Shapes2D.SpriteUv(64, 32, new Vector4(x, y, w, h)));
    }

    [Fact]
    public void SpriteUv_DefaultsToTheWholeTexture()
    {
        Assert.Equal(new Vector4(0, 0, 1, 1), Shapes2D.SpriteUv(64, 32, null));
        Assert.Equal(new Vector4(0, 0, 1, 1), Shapes2D.SpriteUv(0, 0, new Vector4(1, 1, 1, 1)));
    }
}
