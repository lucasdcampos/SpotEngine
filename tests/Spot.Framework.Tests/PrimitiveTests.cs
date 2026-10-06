using System.Numerics;
using Spot.Engine.Graphics;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers procedural primitives: the spec's text form and normalization, the geometry invariants every generated
/// shape must hold (in-range indices, unit normals, outward winding, unmirrored texture coordinates, exact bounds),
/// and loading primitives through the shared cache and the model registry.
/// </summary>
public class PrimitiveTests
{
    public static TheoryData<string> Specs() => new()
    {
        "Cube", "Cube?width=2&height=0.5&depth=3",
        "Sphere", "Sphere?radius=2&segments=7&rings=3",
        "Capsule", "Capsule?radius=0.3&height=1.7&segments=12&rings=1", "Capsule?radius=1",
        "Cylinder", "Cylinder?radius=0.2&height=5&segments=3",
        "Cone", "Cone?radius=2&height=0.5&segments=5",
        "Plane", "Plane?width=4&depth=2&subdivisions=3",
        "Quad", "Quad?width=3&height=2",
    };

    // ----- Spec ------------------------------------------------------------------------------------

    [Fact]
    public void Defaults_MatchTheClassicPrimitives()
    {
        Assert.Equal((1f, 1f, 1f), Size(PrimitiveSpec.For(PrimitiveShape.Cube)));
        Assert.Equal(0.5f, PrimitiveSpec.For(PrimitiveShape.Sphere).Radius);
        Assert.Equal((10f, 10, 10), (PrimitiveSpec.For(PrimitiveShape.Plane).Width, PrimitiveSpec.For(PrimitiveShape.Plane).Subdivisions, 10));
        Assert.Equal((0.5f, 2f), (PrimitiveSpec.For(PrimitiveShape.Capsule).Radius, PrimitiveSpec.For(PrimitiveShape.Capsule).Height));
    }

    [Fact]
    public void ToString_ListsOnlyNonDefaultParametersInCanonicalOrder()
    {
        Assert.Equal("Cube", PrimitiveSpec.For(PrimitiveShape.Cube).ToString());
        PrimitiveSpec capsule = PrimitiveSpec.For(PrimitiveShape.Capsule)
            .With(PrimitiveParameter.Height, 1.7f)
            .With(PrimitiveParameter.Radius, 0.3f);
        Assert.Equal("Capsule?radius=0.3&height=1.7", capsule.ToString());
    }

    [Theory]
    [MemberData(nameof(Specs))]
    public void TextForm_RoundTrips(string text)
    {
        PrimitiveSpec spec = PrimitiveSpec.Parse(text);

        Assert.Equal(text, spec.ToString());
        Assert.Equal(spec, PrimitiveSpec.Parse(spec.ToString()));
    }

    [Fact]
    public void Parse_IgnoresCaseUnknownKeysAndMalformedValues()
    {
        Assert.Equal(PrimitiveSpec.For(PrimitiveShape.Cube), PrimitiveSpec.Parse("cube"));
        Assert.Equal(PrimitiveSpec.For(PrimitiveShape.Sphere), PrimitiveSpec.Parse("SPHERE?colour=red&radius=abc&&"));
        Assert.Equal(2f, PrimitiveSpec.Parse("sphere?RADIUS=2").Radius);
        Assert.False(PrimitiveSpec.TryParse("Teapot", out _));
        Assert.False(PrimitiveSpec.TryParse("3", out _));
        Assert.False(PrimitiveSpec.TryParse("", out _));
        Assert.Throws<FormatException>(() => PrimitiveSpec.Parse("Teapot"));
    }

    [Fact]
    public void Normalize_ClampsAndRounds()
    {
        PrimitiveSpec sphere = PrimitiveSpec.Parse("Sphere?radius=-1&segments=1&rings=0");
        Assert.Equal((0.001f, 3, 2), (sphere.Radius, sphere.Segments, sphere.Rings));

        // A capsule is never shorter than its two caps.
        PrimitiveSpec capsule = PrimitiveSpec.Parse("Capsule?radius=1&height=0.5");
        Assert.Equal(2f, capsule.Height);

        Assert.Equal(0.3333f, PrimitiveSpec.For(PrimitiveShape.Cone).With(PrimitiveParameter.Radius, 1f / 3).Radius);
        Assert.Equal(24, PrimitiveSpec.For(PrimitiveShape.Cone).With(PrimitiveParameter.Segments, 23.6f).Segments);
        Assert.Equal(0.5f, PrimitiveSpec.For(PrimitiveShape.Cone).With(PrimitiveParameter.Radius, float.NaN).Radius);
    }

    [Fact]
    public void With_IgnoresParametersTheShapeDoesNotUse()
    {
        PrimitiveSpec sphere = PrimitiveSpec.For(PrimitiveShape.Sphere);

        Assert.Equal(sphere, sphere.With(PrimitiveParameter.Width, 5));
        Assert.Equal(0, sphere.Width);
        Assert.DoesNotContain(PrimitiveParameter.Width, PrimitiveSpec.ParametersOf(PrimitiveShape.Sphere));
    }

    [Fact]
    public void EveryShape_HasParametersAndDefaults()
    {
        foreach (PrimitiveShape shape in PrimitiveSpec.Shapes)
        {
            Assert.NotEmpty(PrimitiveSpec.ParametersOf(shape));
            Assert.Equal(PrimitiveSpec.For(shape), PrimitiveSpec.For(shape).Normalize());
            Assert.Equal(shape.ToString(), PrimitiveSpec.For(shape).ToString());
        }
    }

    // ----- Geometry --------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Specs))]
    public void Geometry_IsWellFormed(string text)
    {
        MeshData mesh = PrimitiveSpec.Parse(text).Build();
        int count = mesh.Vertices.Length / Mesh.FloatsPerVertex;

        Assert.False(mesh.Skinned);
        Assert.Equal(0, mesh.Vertices.Length % Mesh.FloatsPerVertex);
        Assert.NotEmpty(mesh.Indices);
        Assert.Equal(0, mesh.Indices.Length % 3);
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0u, (uint)count - 1));

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(1.0f, Normal(mesh, i).Length(), 3);
            Vector2 uv = Uv(mesh, i);
            Assert.InRange(uv.X, 0.0f, 1.0f);
            Assert.InRange(uv.Y, 0.0f, 1.0f);
        }
    }

    [Theory]
    [MemberData(nameof(Specs))]
    public void Triangles_FaceOutwardAndMapTheTextureUnmirrored(string text)
    {
        MeshData mesh = PrimitiveSpec.Parse(text).Build();

        for (int t = 0; t < mesh.Indices.Length; t += 3)
        {
            int a = (int)mesh.Indices[t], b = (int)mesh.Indices[t + 1], c = (int)mesh.Indices[t + 2];
            Vector3 face = Vector3.Cross(Position(mesh, b) - Position(mesh, a), Position(mesh, c) - Position(mesh, a));
            Assert.True(face.LengthSquared() > 0, $"{text}: triangle {t / 3} is degenerate");

            // Counter-clockwise seen from outside: the face normal agrees with the vertex normals.
            Vector3 normals = Normal(mesh, a) + Normal(mesh, b) + Normal(mesh, c);
            Assert.True(Vector3.Dot(face, normals) > 0, $"{text}: triangle {t / 3} faces inward");

            // The texture runs the same way round as the geometry (u right, v up), so it is never mirrored.
            Vector2 du = Uv(mesh, b) - Uv(mesh, a), dv = Uv(mesh, c) - Uv(mesh, a);
            Assert.True(du.X * dv.Y - du.Y * dv.X > 0, $"{text}: triangle {t / 3} mirrors the texture");
        }
    }

    [Theory]
    [InlineData("Cube?width=2&height=0.5&depth=3", 2f, 0.5f, 3f)]
    [InlineData("Sphere?radius=2", 4f, 4f, 4f)]
    [InlineData("Capsule?radius=0.3&height=1.7", 0.6f, 1.7f, 0.6f)]
    [InlineData("Cylinder?radius=0.2&height=5", 0.4f, 5f, 0.4f)]
    [InlineData("Cone?radius=2&height=0.5", 4f, 0.5f, 4f)]
    [InlineData("Plane?width=4&depth=2", 4f, 0f, 2f)]
    [InlineData("Quad?width=3&height=2", 3f, 2f, 0f)]
    public void Bounds_MatchTheParametersAndAreCentered(string text, float width, float height, float depth)
    {
        MeshData mesh = PrimitiveSpec.Parse(text).Build();
        (Vector3 min, Vector3 max) = Extent(mesh);

        Assert.Equal(new Vector3(width, height, depth), max - min, new Vector3Comparer(1e-3f));
        Assert.Equal(Vector3.Zero, (min + max) * 0.5f, new Vector3Comparer(1e-3f));
    }

    [Theory]
    [MemberData(nameof(Specs))]
    public void Bounds_ArePredictedWithoutGeneratingTheMesh(string text)
    {
        PrimitiveSpec spec = PrimitiveSpec.Parse(text);
        (Vector3 min, Vector3 max) = Extent(spec.Build());
        Vector3 boundsMin = spec.Bounds.Min, boundsMax = spec.Bounds.Max;

        // The geometry stays inside the ideal shape's box (a coarse circle is a polygon inscribed in it) and
        // matches its height exactly; flat-sided shapes match it exactly.
        Assert.True(Vector3.Min(min, boundsMin - new Vector3(1e-3f)) == boundsMin - new Vector3(1e-3f), $"{min} is outside {boundsMin}");
        Assert.True(Vector3.Max(max, boundsMax + new Vector3(1e-3f)) == boundsMax + new Vector3(1e-3f), $"{max} is outside {boundsMax}");
        Assert.Equal(boundsMax.Y - boundsMin.Y, max.Y - min.Y, 3);
        if (spec.Shape is PrimitiveShape.Cube or PrimitiveShape.Plane or PrimitiveShape.Quad)
        {
            Assert.Equal(boundsMin, min, new Vector3Comparer(1e-3f));
            Assert.Equal(boundsMax, max, new Vector3Comparer(1e-3f));
        }
    }

    [Fact]
    public void Capsule_SidesAreStraightBetweenTheCaps()
    {
        MeshData mesh = PrimitiveSpec.Parse("Capsule?radius=0.5&height=3").Build();

        // Every vertex lies on the swept profile: within the radius of the segment between the cap centers.
        for (int i = 0; i < mesh.Vertices.Length / Mesh.FloatsPerVertex; i++)
        {
            Vector3 p = Position(mesh, i);
            var nearest = new Vector3(0, Math.Clamp(p.Y, -1.0f, 1.0f), 0);
            Assert.Equal(0.5f, Vector3.Distance(p, nearest), 3);
        }
    }

    [Fact]
    public void Cone_NarrowsToItsApex()
    {
        MeshData mesh = PrimitiveSpec.Parse("Cone?radius=1&height=2").Build();

        for (int i = 0; i < mesh.Vertices.Length / Mesh.FloatsPerVertex; i++)
        {
            Vector3 p = Position(mesh, i);
            float allowed = (1.0f - p.Y) * 0.5f; // radius 1 at the base (y = -1), 0 at the apex (y = 1)
            Assert.True(new Vector2(p.X, p.Z).Length() <= allowed + 1e-4f, $"vertex {i} at {p} is outside the cone");
        }
    }

    [Fact]
    public void Segments_ControlTheResolution()
    {
        int Triangles(string text) => PrimitiveSpec.Parse(text).Build().Indices.Length / 3;

        Assert.True(Triangles("Sphere?segments=64") > Triangles("Sphere?segments=8"));
        Assert.Equal(2 * 3 * 3, Triangles("Plane?subdivisions=3"));
        Assert.Equal(12, Triangles("Cube"));
        Assert.Equal(2, Triangles("Quad"));
        Assert.Equal(3 * 2 + 3 + 3, Triangles("Cylinder?segments=3")); // side quads + two caps
    }

    // ----- Models ----------------------------------------------------------------------------------

    [Fact]
    public void Get_SharesOneModelPerSpec_AndCreateDoesNot()
    {
        Renderer.Init(new RecordingGraphicsDevice());

        Model shared = PrimitiveModelFactory.Get("Capsule?radius=0.3");
        Assert.Same(shared, PrimitiveModelFactory.Get(PrimitiveSpec.For(PrimitiveShape.Capsule).With(PrimitiveParameter.Radius, 0.3f)));
        Assert.NotSame(shared, PrimitiveModelFactory.Get("Capsule"));
        Assert.NotSame(shared, PrimitiveModelFactory.Create("Capsule?radius=0.3"));
        Assert.Equal("primitive:Capsule?radius=0.3", shared.SourcePath);
    }

    [Fact]
    public void Get_RebuildsAfterTheDeviceChanges()
    {
        Renderer.Init(new RecordingGraphicsDevice());
        Model before = PrimitiveModelFactory.Get("Cone");

        Renderer.Init(new RecordingGraphicsDevice());

        Assert.NotSame(before, PrimitiveModelFactory.Get("Cone"));
    }

    [Fact]
    public void ModelImporter_LoadsPrimitiveReferencesFromTheSharedCache()
    {
        Renderer.Init(new RecordingGraphicsDevice());

        Model loaded = ModelImporter.Load("primitive:capsule?height=3");

        Assert.Same(PrimitiveModelFactory.Get("Capsule?height=3"), loaded);
        Assert.Same(loaded, ModelImporter.RequestAsync("PRIMITIVE:Capsule?height=3"));
        Assert.True(ModelImporter.IsProvided("primitive:Cube"));
        Assert.True(ModelImporter.CanLoad("primitive:Cube"));
        Assert.Equal(new Vector3(1, 3, 1), loaded.LocalBounds.Max - loaded.LocalBounds.Min, new Vector3Comparer(1e-3f));
    }

    [Fact]
    public void ModelImporter_ReportsAnUnknownPrimitiveOnce()
    {
        Renderer.Init(new RecordingGraphicsDevice());
        using RecordingLogSink log = RecordingLogSink.Capture();

        Assert.Throws<ArgumentException>(() => ModelImporter.Load("primitive:Teapot"));
        Assert.Null(ModelImporter.RequestAsync("primitive:Teapot"));
        Assert.Null(ModelImporter.RequestAsync("primitive:Teapot"));

        Assert.Single(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Teapot", StringComparison.Ordinal));
    }

    [Fact]
    public void RegisterProvider_ServesItsPrefixAndReplacesAnEarlierOne()
    {
        Renderer.Init(new RecordingGraphicsDevice());
        Model first = PrimitiveModelFactory.Create("Cube");
        Model second = PrimitiveModelFactory.Create("Sphere");

        ModelImporter.RegisterProvider("test-provider:", _ => first);
        Assert.Same(first, ModelImporter.Load("test-provider:anything"));

        ModelImporter.RegisterProvider("TEST-PROVIDER:", reference => reference.EndsWith("ball", StringComparison.Ordinal) ? second : first);
        Assert.Same(second, ModelImporter.Load("test-provider:ball"));
        Assert.Same(second, ModelImporter.RequestAsync("test-provider:ball"));
    }

    [Fact]
    public void LegacyNames_StillCreateTheClassicPrimitives()
    {
        Renderer.Init(new RecordingGraphicsDevice());

        Model cube = PrimitiveModelFactory.Create("cube");
        Model plane = PrimitiveModelFactory.Create("plane");

        Assert.Equal(Vector3.One, cube.LocalBounds.Max - cube.LocalBounds.Min, new Vector3Comparer(1e-4f));
        Assert.Equal(new Vector3(10, 0, 10), plane.LocalBounds.Max - plane.LocalBounds.Min, new Vector3Comparer(1e-4f));
        Assert.Throws<ArgumentException>(() => PrimitiveModelFactory.Create("teapot"));
    }

    // ----- Helpers ---------------------------------------------------------------------------------

    private static (float, float, float) Size(PrimitiveSpec spec) => (spec.Width, spec.Height, spec.Depth);

    private static Vector3 Position(MeshData mesh, int i)
    {
        int o = i * Mesh.FloatsPerVertex;
        return new Vector3(mesh.Vertices[o], mesh.Vertices[o + 1], mesh.Vertices[o + 2]);
    }

    private static Vector3 Normal(MeshData mesh, int i)
    {
        int o = i * Mesh.FloatsPerVertex + 3;
        return new Vector3(mesh.Vertices[o], mesh.Vertices[o + 1], mesh.Vertices[o + 2]);
    }

    private static Vector2 Uv(MeshData mesh, int i)
    {
        int o = i * Mesh.FloatsPerVertex + 6;
        return new Vector2(mesh.Vertices[o], mesh.Vertices[o + 1]);
    }

    private static (Vector3 Min, Vector3 Max) Extent(MeshData mesh)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < mesh.Vertices.Length / Mesh.FloatsPerVertex; i++)
        {
            min = Vector3.Min(min, Position(mesh, i));
            max = Vector3.Max(max, Position(mesh, i));
        }

        return (min, max);
    }

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= tolerance;

        public int GetHashCode(Vector3 obj) => 0;
    }
}
