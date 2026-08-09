using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spot.Assets;
using Spot.Scenes;
using Xunit;

namespace Spot.Engine.Tests;

public class ModelInstantiatorTests
{
    // A two-part OBJ: each object is a single triangle with its own material, so a correct import yields
    // two submeshes referencing two different material slots. Companion .mtl gives the slots distinct colors.
    private const string Obj =
        "mtllib model.mtl\n" +
        "v 0 0 0\nv 1 0 0\nv 0 1 0\n" +
        "v 2 0 0\nv 3 0 0\nv 2 1 0\n" +
        "vn 0 0 1\n" +
        "o Part1\nusemtl RedMat\nf 1//1 2//1 3//1\n" +
        "o Part2\nusemtl BlueMat\nf 4//1 5//1 6//1\n";

    private const string Mtl =
        "newmtl RedMat\nKd 1.0 0.0 0.0\n" +
        "newmtl BlueMat\nKd 0.0 0.0 1.0\n";

    private static string WriteModel(string dir)
    {
        string objPath = Path.Combine(dir, "model.obj");
        File.WriteAllText(objPath, Obj);
        File.WriteAllText(Path.Combine(dir, "model.mtl"), Mtl);
        return objPath;
    }

    [Fact]
    public void ImportSceneInfo_ReportsSubmeshesAndDistinctMaterialSlots()
    {
        using var temp = new TempDir();
        string objPath = WriteModel(temp.Path);

        ModelSceneInfo info = new AssimpModelImporter().ImportSceneInfo(objPath);

        Assert.Equal(2, info.MeshMaterialIndex.Count);
        Assert.NotEqual(info.MeshMaterialIndex[0], info.MeshMaterialIndex[1]);

        var meshIndices = new List<int>();
        Collect(info.Root, meshIndices);
        Assert.Equal(new[] { 0, 1 }, meshIndices.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void Instantiate_BuildsHierarchyWithPerPartSubmeshesAndMaterials()
    {
        using var temp = new TempDir();
        string objPath = WriteModel(temp.Path);
        var scene = new Scene();

        Entity? root = ModelInstantiator.Instantiate(scene, objPath);

        Assert.NotNull(root);
        Assert.Equal("model", root!.Value.Name);

        List<MeshComponent> parts = scene.View<MeshComponent>()
            .Select(e => e.GetComponent<MeshComponent>())
            .ToList();

        // One renderer per submesh, each pointing at the shared model but a different submesh.
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Equal(objPath, p.ModelPath));
        Assert.Equal(new[] { 0, 1 }, parts.Select(p => p.SubmeshIndex).OrderBy(i => i).ToArray());

        // Each part got its own material assigned, and the two differ.
        Assert.All(parts, p => Assert.False(string.IsNullOrEmpty(p.MaterialPath)));
        Assert.Equal(2, parts.Select(p => p.MaterialPath).Distinct().Count());

        // The materials were written into the <Model>_Materials folder next to the source.
        string matDir = Path.Combine(temp.Path, "model_Materials");
        Assert.True(Directory.Exists(matDir));
        Assert.NotEmpty(Directory.GetFiles(matDir, "*.sptmat"));
    }

    [Fact]
    public void Instantiate_WithMissingFile_ReturnsNullAndDoesNotThrow()
    {
        using var temp = new TempDir();
        var scene = new Scene();

        Entity? root = ModelInstantiator.Instantiate(scene, Path.Combine(temp.Path, "nope.obj"));

        Assert.Null(root);
        Assert.Empty(scene.View<MeshComponent>());
    }

    private static void Collect(ModelNodeInfo node, List<int> accumulator)
    {
        accumulator.AddRange(node.MeshIndices);
        foreach (ModelNodeInfo child in node.Children)
        {
            Collect(child, accumulator);
        }
    }
}
