using System.Numerics;
using Spot.Engine.Assimp;
using Spot.Engine.Graphics;
using Spot.Tests;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers generated images (solid, flat normal, checkerboard, grid, soft dot), PNG encoding round trips, and
/// exporting generated meshes as OBJ files a model importer reads back.
/// </summary>
public class ProceduralImageTests
{
    [Fact]
    public void Solid_FillsEveryPixel()
    {
        Image image = ProceduralImages.Solid(new Vector4(1, 0.5f, 0, 0.25f), size: 3);

        Assert.Equal((3, 3), (image.Width, image.Height));
        for (int i = 0; i < image.Pixels.Length; i += 4)
        {
            Assert.Equal(new byte[] { 255, 128, 0, 64 }, image.Pixels[i..(i + 4)]);
        }
    }

    [Fact]
    public void FlatNormal_PointsStraightOut()
    {
        Image image = ProceduralImages.FlatNormal();

        Assert.Equal(new byte[] { 128, 128, 255, 255 }, image.Pixels[..4]);
    }

    [Fact]
    public void Checkerboard_AlternatesCells()
    {
        Image image = ProceduralImages.Checkerboard(size: 8, cells: 2, light: Vector4.One, dark: new Vector4(0, 0, 0, 1));

        Assert.Equal(255, Pixel(image, 0, 0)[0]);
        Assert.Equal(0, Pixel(image, 4, 0)[0]);
        Assert.Equal(0, Pixel(image, 0, 4)[0]);
        Assert.Equal(255, Pixel(image, 7, 7)[0]);
    }

    [Fact]
    public void Checkerboard_DefaultsMatchTheClassicDebugTexture()
    {
        Image image = ProceduralImages.Checkerboard();

        Assert.Equal((256, 256), (image.Width, image.Height));
        Assert.Equal(new byte[] { 72, 72, 72, 255 }, Pixel(image, 0, 0));
        Assert.Equal(new byte[] { 48, 48, 48, 255 }, Pixel(image, 32, 0));
    }

    [Fact]
    public void Grid_DrawsLinesOnCellBoundariesAndTileEdges()
    {
        var background = new Vector4(0, 0, 0, 1);
        Image image = ProceduralImages.Grid(size: 64, cells: 4, background: background, line: Vector4.One);

        Assert.Equal(255, Pixel(image, 0, 10)[0]);   // tile edge
        Assert.Equal(255, Pixel(image, 63, 10)[0]);  // opposite edge, so tiles join into one line
        Assert.Equal(255, Pixel(image, 16, 10)[0]);  // cell boundary
        Assert.Equal(0, Pixel(image, 8, 8)[0]);      // inside a cell
        Assert.Equal(0, Pixel(image, 40, 40)[0]);
    }

    [Fact]
    public void SoftDot_IsOpaqueAtTheCenterAndClearAtTheCorners()
    {
        Image image = ProceduralImages.SoftDot(65);

        Assert.Equal(255, Pixel(image, 32, 32)[3]);
        Assert.Equal(0, Pixel(image, 0, 0)[3]);
        Assert.True(Pixel(image, 48, 32)[3] is > 0 and < 255);
        Assert.All(new[] { Pixel(image, 32, 32)[0], Pixel(image, 0, 0)[0] }, c => Assert.Equal(255, c));
    }

    [Fact]
    public void Generators_RejectInvalidSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralImages.Solid(Vector4.One, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralImages.Checkerboard(cells: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralImages.Grid(size: -1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Png_RoundTripsThroughTheDecoder(bool flip)
    {
        var pixels = new byte[5 * 3 * 4];
        new Random(7).NextBytes(pixels);
        var image = new Image(5, 3, pixels);

        Image decoded = Image.FromBytes(image.EncodePng(flip), flip);

        Assert.Equal((5, 3), (decoded.Width, decoded.Height));
        Assert.Equal(pixels, decoded.Pixels);
    }

    [Fact]
    public void Png_StoresRowsTopDown()
    {
        // Bottom-up texture rows: row 0 (the bottom) is red, row 1 (the top) is blue.
        var image = new Image(1, 2, new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 });

        Image topDown = Image.FromBytes(image.EncodePng(), flipVertically: false);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, topDown.Pixels[..4]);
    }

    [Fact]
    public void SavePng_WritesAFileTheDecoderReads()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "nested", "grid.png");
        Image grid = ProceduralImages.Grid(size: 32, cells: 2);

        grid.SavePng(path);

        Assert.Equal(grid.Pixels, Image.FromFile(path).Pixels);
    }

    [Theory]
    [InlineData("Cube")]
    [InlineData("Capsule?radius=0.3&height=1.7&segments=12")]
    [InlineData("Plane?subdivisions=2")]
    public void Obj_ExportReimportsWithTheSameShape(string text)
    {
        MeshData mesh = PrimitiveSpec.Parse(text).Build();
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "shape.obj");
        File.WriteAllText(path, MeshExport.ToObj(mesh, "Shape"));

        CookedModel imported = new AssimpModelImporter().ImportModel(path);

        MeshData back = Assert.Single(imported.Submeshes);
        Assert.Equal(mesh.Indices.Length, back.Indices.Length);
        Assert.Equal(Extent(mesh), Extent(back));
    }

    [Fact]
    public void Obj_WritesEveryAttributeWithOneBasedIndices()
    {
        string obj = MeshExport.ToObj(PrimitiveSpec.Parse("Quad").Build(), "Card");
        string[] lines = obj.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("o Card", lines);
        Assert.Equal(4, lines.Count(l => l.StartsWith("v ", StringComparison.Ordinal)));
        Assert.Equal(4, lines.Count(l => l.StartsWith("vt ", StringComparison.Ordinal)));
        Assert.Equal(4, lines.Count(l => l.StartsWith("vn ", StringComparison.Ordinal)));
        Assert.Equal(new[] { "f 1/1/1 2/2/2 3/3/3", "f 1/1/1 3/3/3 4/4/4" }, lines.Where(l => l.StartsWith("f ", StringComparison.Ordinal)));
    }

    private static byte[] Pixel(Image image, int x, int y)
    {
        int i = (y * image.Width + x) * 4;
        return image.Pixels[i..(i + 4)];
    }

    private static (Vector3, Vector3) Extent(MeshData mesh)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int o = 0; o < mesh.Vertices.Length; o += Mesh.FloatsPerVertex)
        {
            var p = new Vector3(mesh.Vertices[o], mesh.Vertices[o + 1], mesh.Vertices[o + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return (Round(min), Round(max));
    }

    private static Vector3 Round(Vector3 v) => new(MathF.Round(v.X, 3), MathF.Round(v.Y, 3), MathF.Round(v.Z, 3));
}
