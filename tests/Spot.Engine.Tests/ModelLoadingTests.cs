using System.IO;
using System.Numerics;
using Spot.Assets;
using Spot.Core;
using Spot.Engine.Tests.Fakes;
using Spot.Rendering;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the framework's model registry (importers by extension, reference resolution, caching, async
/// loading) and the engine's plug-ins on top of it (guid resolution, cooked meshes, material extraction).
/// </summary>
public class ModelLoadingTests : IDisposable
{
    // One triangle in the rigid layout: position (3), normal (3), uv (2).
    private static readonly float[] Triangle =
    {
        0, 0, 0, 0, 0, 1, 0, 0,
        1, 0, 0, 0, 0, 1, 1, 0,
        0, 1, 0, 0, 0, 1, 0, 1,
    };

    private readonly Func<string, string?>? _previousResolver = ModelImporter.ReferenceResolver;

    public ModelLoadingTests()
    {
        Renderer.Init(new RecordingGraphicsDevice());
    }

    public void Dispose() => ModelImporter.ReferenceResolver = _previousResolver;

    private static CookedModel TriangleModel() => new(new[] { new MeshData(Triangle, new uint[] { 0, 1, 2 }) }, null);

    [Fact]
    public void Load_UsesTheImporterForTheExtensionAndCaches()
    {
        var importer = new FakeImporter(".fakeload");
        ModelImporter.Register(importer);
        ModelImporter.ReferenceResolver = reference => Path.Combine(Path.GetTempPath(), "virtual", reference);

        Model first = ModelImporter.Load("ship.fakeload");
        Model second = ModelImporter.Load("ship.fakeload");

        Assert.Same(first, second);
        Assert.Equal(1, importer.Imports);
        Assert.Single(first.Meshes);
        Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "virtual", "ship.fakeload")), first.SourcePath);
        Assert.True(ModelImporter.CanLoad("other.fakeload"));
    }

    [Fact]
    public void Load_UnresolvedReferenceThrowsFileNotFound()
    {
        ModelImporter.ReferenceResolver = _ => null;

        Assert.Throws<FileNotFoundException>(() => ModelImporter.Load("guid:does-not-exist"));
    }

    [Fact]
    public void Load_UnknownFormatThrowsNotSupported()
    {
        ModelImporter.ReferenceResolver = reference => reference;

        Assert.Throws<NotSupportedException>(() => ModelImporter.Load("model.unknownformat"));
        Assert.False(ModelImporter.CanLoad("model.unknownformat"));
    }

    [Fact]
    public void Load_PrimitivesBypassTheResolverAndAreCached()
    {
        ModelImporter.ReferenceResolver = _ => throw new InvalidOperationException("primitives are never resolved");

        Model cube = ModelImporter.Load("primitive:Cube");

        Assert.Same(cube, ModelImporter.Load("primitive:Cube"));
        Assert.Equal("primitive:Cube", cube.SourcePath);
        Assert.NotEmpty(cube.Meshes);
    }

    [Fact]
    public void RequestAsync_ParsesOnAWorkerAndCompletesOnProcessPendingUploads()
    {
        var importer = new FakeImporter(".fakeasync");
        ModelImporter.Register(importer);
        ModelImporter.ReferenceResolver = reference => Path.Combine(Path.GetTempPath(), reference);

        Model? model = ModelImporter.RequestAsync("async.fakeasync");
        Assert.Null(model);

        model = PumpUntilLoaded("async.fakeasync");

        Assert.NotNull(model);
        Assert.Equal(1, importer.CpuImports);
        Assert.Equal(0, importer.Imports); // the async path builds the GPU model itself from the CPU data
        Assert.Same(model, ModelImporter.RequestAsync("async.fakeasync"));
    }

    [Fact]
    public void RequestAsync_FailedParseIsLoggedOnceAndNeverRetried()
    {
        var importer = new FakeImporter(".fakebroken") { Fail = true };
        ModelImporter.Register(importer);
        ModelImporter.ReferenceResolver = reference => Path.Combine(Path.GetTempPath(), reference);
        using RecordingLogSink log = RecordingLogSink.Capture();

        Assert.Null(ModelImporter.RequestAsync("broken.fakebroken"));
        PumpUntil(() => log.Contains(LogLevel.Error, "broken.fakebroken"));
        Assert.Null(ModelImporter.RequestAsync("broken.fakebroken"));
        ModelImporter.ProcessPendingUploads();

        Assert.Equal(1, importer.CpuImports);
        Assert.Single(log.Entries, e => e.Message.Contains("broken.fakebroken"));
    }

    [Fact]
    public void RequestAsync_UnresolvedOrUnsupportedReferencesReturnNullAndLogOnce()
    {
        using RecordingLogSink log = RecordingLogSink.Capture();

        ModelImporter.ReferenceResolver = _ => null;
        Assert.Null(ModelImporter.RequestAsync("guid:unresolved-async"));
        Assert.Null(ModelImporter.RequestAsync("guid:unresolved-async"));

        ModelImporter.ReferenceResolver = reference => Path.Combine(Path.GetTempPath(), reference);
        Assert.Null(ModelImporter.RequestAsync("model.nosuchformat"));
        Assert.Null(ModelImporter.RequestAsync("model.nosuchformat"));

        Assert.Single(log.Entries, e => e.Message.Contains("guid:unresolved-async"));
        Assert.Single(log.Entries, e => e.Message.Contains(".nosuchformat"));
    }

    [Fact]
    public void EngineAssets_LoadsGuidReferencesFromCookedMeshes()
    {
        using var dir = new TempDir();
        string cooked = Path.Combine(dir.Path, "ship.sptmesh");
        File.WriteAllBytes(cooked, SpMesh.Write(TriangleModel()));
        Func<string, string?>? previousContent = AssetPath.ContentResolver;
        try
        {
            AssetPath.ContentResolver = reference => reference == "guid:ship-mesh" ? cooked : null;
            EngineAssets.Install();
            ModelImporter.ReferenceResolver = EngineAssets.ResolveModelReference;

            Model model = ModelImporter.Load("guid:ship-mesh");

            Assert.Single(model.Meshes);
            Assert.Equal(3u, model.Meshes[0].IndexCount);
        }
        finally
        {
            AssetPath.ContentResolver = previousContent;
        }
    }

    [Fact]
    public void EngineAssets_ResolvesGuidsToContentAndPathsAgainstTheAssetRoot()
    {
        string previousRoot = AssetPath.Root;
        Func<string, string?>? previousContent = AssetPath.ContentResolver;
        using var dir = new TempDir();
        try
        {
            AssetPath.Root = dir.Path;
            AssetPath.ContentResolver = reference => reference == "guid:abc" ? "cooked/abc.sptmesh" : null;

            Assert.Equal("cooked/abc.sptmesh", EngineAssets.ResolveModelReference("guid:abc"));
            Assert.Null(EngineAssets.ResolveModelReference("guid:missing"));
            Assert.Equal(Path.Combine(dir.Path, "Models/a.fbx"), EngineAssets.ResolveModelReference("Models/a.fbx"));
        }
        finally
        {
            AssetPath.Root = previousRoot;
            AssetPath.ContentResolver = previousContent;
            Spot.IO.FileSystem.PathResolver = null;
        }
    }

    [Fact]
    public void SpMeshImporter_HandlesCookedMeshes()
    {
        using var dir = new TempDir();
        string cooked = Path.Combine(dir.Path, "m.sptmesh");
        File.WriteAllBytes(cooked, SpMesh.Write(TriangleModel()));
        var importer = new SpMeshModelImporter();

        Assert.Equal(new[] { ".sptmesh" }, importer.SupportedExtensions);
        Assert.Single(importer.ImportMeshData(cooked));
        Assert.Single(importer.ImportModel(cooked).Submeshes);
        Assert.Single(importer.Import(cooked).Meshes);
    }

    [Fact]
    public void ModelMaterials_WritesOneMaterialPerSlotAndKeepsExistingOnes()
    {
        using var dir = new TempDir();
        string objPath = Path.Combine(dir.Path, "model.obj");
        File.WriteAllText(objPath,
            "mtllib model.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nv 2 0 0\nv 3 0 0\nv 2 1 0\nvn 0 0 1\n" +
            "o A\nusemtl RedMat\nf 1//1 2//1 3//1\no B\nusemtl BlueMat\nf 4//1 5//1 6//1\n");
        File.WriteAllText(Path.Combine(dir.Path, "model.mtl"), "newmtl RedMat\nKd 1 0 0\nnewmtl BlueMat\nKd 0 0 1\n");
        string outDir = Path.Combine(dir.Path, "mats");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "BlueMat.sptmat"), "{ \"keep\": true }");

        IReadOnlyDictionary<int, string> slots = ModelMaterials.ExtractPerSlot(objPath, outDir);

        Assert.Contains(slots.Values, p => p.EndsWith("RedMat.sptmat"));
        Assert.Contains(slots.Values, p => p.EndsWith("BlueMat.sptmat"));
        Assert.Equal("{ \"keep\": true }", File.ReadAllText(Path.Combine(outDir, "BlueMat.sptmat")));
        Material red = Material.Load(Path.Combine(outDir, "RedMat.sptmat"));
        Assert.Equal(new Vector4(1, 0, 0, 1), red.Color);
    }

    [Fact]
    public void AssimpReadMaterials_SkipsSlotsTheFilterDeclines()
    {
        using var dir = new TempDir();
        string objPath = Path.Combine(dir.Path, "one.obj");
        File.WriteAllText(objPath, "mtllib one.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl Green\nf 1 2 3\n");
        File.WriteAllText(Path.Combine(dir.Path, "one.mtl"), "newmtl Green\nKd 0 1 0\n");

        ImportedMaterial read = Assert.Single(AssimpModelImporter.ReadMaterials(objPath, dir.Path),
            m => m.Name == "Green");
        ImportedMaterial skipped = Assert.Single(AssimpModelImporter.ReadMaterials(objPath, dir.Path, _ => false),
            m => m.Name == "Green");

        Assert.Equal(new Vector4(0, 1, 0, 1), read.Color);
        Assert.Null(skipped.Color);
        Assert.Null(skipped.TexturePath);
    }

    private static Model? PumpUntilLoaded(string reference)
    {
        Model? model = null;
        PumpUntil(() => (model = ModelImporter.RequestAsync(reference)) is not null);
        return model;
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            ModelImporter.ProcessPendingUploads();
            if (condition())
            {
                return;
            }

            Thread.Sleep(5);
        }

        Assert.Fail("Timed out waiting for the background model load.");
    }

    private sealed class FakeImporter : IModelImporter
    {
        private readonly string _extension;
        private int _imports;
        private int _cpuImports;

        public FakeImporter(string extension) => _extension = extension;

        public bool Fail { get; init; }

        public int Imports => Volatile.Read(ref _imports);

        public int CpuImports => Volatile.Read(ref _cpuImports);

        public IEnumerable<string> SupportedExtensions => new[] { _extension };

        public IReadOnlyList<MeshData> ImportMeshData(string path) => ImportModel(path).Submeshes;

        public CookedModel ImportModel(string path)
        {
            Interlocked.Increment(ref _cpuImports);
            if (Fail)
            {
                throw new InvalidDataException("corrupt model");
            }

            return TriangleModel();
        }

        public Model Import(string path)
        {
            Interlocked.Increment(ref _imports);
            return ModelImporter.BuildModel(TriangleModel());
        }
    }
}
