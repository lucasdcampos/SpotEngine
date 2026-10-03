using System.IO;
using System.Linq;
using Spot.DebugUI.UI;
using Spot.DebugUI.Undo;
using Spot.Engine.Assets;
using Spot.Engine.Scenes;
using Spot.Tests;

namespace Spot.Engine.Tests;

/// <summary>Covers applying a material dropped on a mesh in the editor's viewport or Hierarchy.</summary>
public class MaterialDropTests
{
    private static string Builtin(string name) =>
        BuiltinAssets.OfKind(BuiltinAssetKind.Material).First(a => a.Name == name).Reference;

    [Fact]
    public void TargetsFor_AMeshIsItsOwnTarget()
    {
        var scene = new Scene();
        Entity cube = scene.Instantiate("Cube");
        cube.AddComponent(new MeshComponent());
        scene.Instantiate("Part").SetParent(cube);

        Assert.Equal(new[] { cube }, MaterialDrop.TargetsFor(cube));
    }

    [Fact]
    public void TargetsFor_AnEntityWithoutAMeshPaintsEveryMeshBeneathIt()
    {
        var scene = new Scene();
        Entity root = scene.Instantiate("Robot");
        Entity body = scene.Instantiate("Body");
        body.SetParent(root);
        body.AddComponent(new MeshComponent());
        Entity arm = scene.Instantiate("Arm");
        arm.SetParent(body);
        arm.AddComponent(new MeshComponent());
        scene.Instantiate("Socket").SetParent(root);

        Assert.Equal(new[] { arm, body }, MaterialDrop.TargetsFor(root).OrderBy(e => e.Name));
        Assert.Empty(MaterialDrop.TargetsFor(scene.Instantiate("Empty")));
    }

    [Fact]
    public void Apply_AssignsTheMaterialAsOneUndoStep()
    {
        using var dir = new TempDir();
        string materialPath = Path.Combine(dir.Path, "Rock.sptmat");
        File.WriteAllText(materialPath, "{}");

        var scene = new Scene();
        var history = new UndoHistory();
        Entity a = scene.Instantiate("A");
        a.AddComponent(new MeshComponent { MaterialPath = Builtin("Grid") });
        Entity b = scene.Instantiate("B");
        b.AddComponent(new MeshComponent());

        int changed = MaterialDrop.Apply(new[] { a, b }, materialPath, history);

        string? reference = AssetDatabase.ToGuidRef(materialPath);
        Assert.Equal(2, changed);
        Assert.Equal(reference, a.GetComponent<MeshComponent>().MaterialPath);
        Assert.Equal(reference, b.GetComponent<MeshComponent>().MaterialPath);
        Assert.Equal(1, history.Count);
        Assert.Equal("Assign Material 'Rock'", history.UndoLabel);

        Assert.True(history.Undo());
        Assert.Equal(Builtin("Grid"), a.GetComponent<MeshComponent>().MaterialPath);
        Assert.Null(b.GetComponent<MeshComponent>().MaterialPath);
    }

    [Fact]
    public void Apply_DropsTheLoadedMaterialSoTheNewOneIsResolved()
    {
        var scene = new Scene();
        Entity cube = scene.Instantiate("Cube");
        var mesh = new MeshComponent { MaterialPath = Builtin("Grid"), Material = new Material() };
        cube.AddComponent(mesh);

        MaterialDrop.Apply(new[] { cube }, Builtin("Checker"), new UndoHistory());

        Assert.Equal(Builtin("Checker"), mesh.MaterialPath);
        Assert.Null(mesh.Material);
    }

    [Fact]
    public void Apply_SkipsMeshesAlreadyUsingTheMaterialAndNonMeshes()
    {
        var scene = new Scene();
        var history = new UndoHistory();
        Entity cube = scene.Instantiate("Cube");
        cube.AddComponent(new MeshComponent { MaterialPath = Builtin("Grid") });
        Entity empty = scene.Instantiate("Empty");

        Assert.Equal(0, MaterialDrop.Apply(new[] { cube, empty }, Builtin("Grid"), history));
        Assert.Equal(0, history.Count);
    }
}
