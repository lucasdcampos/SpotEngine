using System.IO;
using System.Numerics;
using Spot.Engine.Assets;
using Spot.Framework.Graphics;
using Spot.Tests;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the engine's plug-ins to the framework model registry: guid resolution, cooked meshes and
/// material extraction.
/// </summary>
public class EngineModelLoadingTests : IDisposable
{
    // One triangle in the rigid layout: position (3), normal (3), uv (2).
    private static readonly float[] Triangle =
    {
        0, 0, 0, 0, 0, 1, 0, 0,
        1, 0, 0, 0, 0, 1, 1, 0,
        0, 1, 0, 0, 0, 1, 0, 1,
    };

    private readonly Func<string, string?>? _previousResolver = ModelImporter.ReferenceResolver;

    public EngineModelLoadingTests()
    {
        Renderer.Init(new RecordingGraphicsDevice());
    }

    public void Dispose() => ModelImporter.ReferenceResolver = _previousResolver;

    private static CookedModel TriangleModel() => new(new[] { new MeshData(Triangle, new uint[] { 0, 1, 2 }) }, null);

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
            Spot.Framework.IO.FileSystem.PathResolver = null;
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
}
