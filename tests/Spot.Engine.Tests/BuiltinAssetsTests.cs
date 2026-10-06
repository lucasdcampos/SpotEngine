using System.Numerics;
using System.Text.Json.Nodes;
using Spot.Engine.Assets;
using Spot.Engine.Graphics;
using Spot.Engine.Scenes;
using Spot.Engine;
using Spot.Tests;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the built-in asset catalog: stable <c>builtin:</c> references and their legacy aliases, shared
/// generated meshes/textures/materials that survive misuse and device changes, editable copies, and the engine
/// paths that consume them (asset paths, model provider, materials, scenes, rendering).
/// </summary>
public class BuiltinAssetsTests
{
    private static RecordingGraphicsDevice Install()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        EngineAssets.Install();
        return device;
    }

    // ----- Catalog and references ------------------------------------------------------------------

    [Fact]
    public void Catalog_HasEveryPrimitiveTextureAndMaterial_WithUniqueCanonicalReferences()
    {
        Assert.Equal(PrimitiveSpec.Shapes.Count, BuiltinAssets.OfKind(BuiltinAssetKind.Mesh).Count());
        Assert.Equal(
            new[] { "White", "Black", "FlatNormal", "Checker", "Grid", "SoftDot" },
            BuiltinAssets.OfKind(BuiltinAssetKind.Texture).Select(a => a.Name));
        Assert.Equal(new[] { "Default", "Checker", "Grid" }, BuiltinAssets.OfKind(BuiltinAssetKind.Material).Select(a => a.Name));

        Assert.Equal(BuiltinAssets.All.Count, BuiltinAssets.All.Select(a => a.Reference).Distinct().Count());
        foreach (BuiltinAsset asset in BuiltinAssets.All)
        {
            Assert.StartsWith(BuiltinAssets.Scheme, asset.Reference);
            Assert.Equal(asset.Reference, BuiltinAssets.Canonicalize(asset.Reference));
            Assert.True(BuiltinAssets.TryGet(asset.Reference, out BuiltinAsset found));
            Assert.Same(asset, found);
            Assert.False(string.IsNullOrWhiteSpace(asset.Description));
        }
    }

    [Theory]
    [InlineData("primitive:Cube", "builtin:Mesh/Cube")]
    [InlineData("primitive:sphere", "builtin:Mesh/Sphere")]
    [InlineData("primitive:Capsule?height=1.70&radius=0.30", "builtin:Mesh/Capsule?radius=0.3&height=1.7")]
    [InlineData("editor:Checkerboard", "builtin:Material/Checker")]
    [InlineData("builtin:mesh/cone", "builtin:Mesh/Cone")]
    [InlineData("BUILTIN:Texture/grid", "builtin:Texture/Grid")]
    [InlineData("builtin:Mesh/Cube?width=1", "builtin:Mesh/Cube")]
    public void Canonicalize_RewritesAliasesAndNormalizes(string reference, string canonical)
    {
        Assert.Equal(canonical, BuiltinAssets.Canonicalize(reference));
    }

    [Theory]
    [InlineData("Models/ship.fbx")]
    [InlineData("guid:0123456789abcdef0123456789abcdef")]
    [InlineData("builtin:Mesh/Teapot")]
    [InlineData("builtin:Gizmo/Arrow")]
    [InlineData("editor:Unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void Canonicalize_LeavesEverythingElseAlone(string? reference)
    {
        Assert.Equal(reference, BuiltinAssets.Canonicalize(reference));
    }

    [Fact]
    public void TryGet_IgnoresMeshParametersAndRejectsUnknownNames()
    {
        Assert.True(BuiltinAssets.TryGet("builtin:Mesh/Capsule?radius=2", out BuiltinAsset capsule));
        Assert.Equal(("Capsule", BuiltinAssetKind.Mesh), (capsule.Name, capsule.Kind));

        Assert.False(BuiltinAssets.TryGet("builtin:Mesh/Teapot", out _));
        Assert.False(BuiltinAssets.TryGet("builtin:Mesh", out _));
        Assert.False(BuiltinAssets.TryGet("Textures/grid.png", out _));
    }

    [Fact]
    public void BuiltinReferences_AreNeverTreatedAsFiles()
    {
        AssetPath.Root = Path.Combine(Path.GetTempPath(), "spot-builtin-root");
        try
        {
            foreach (string reference in new[] { "builtin:Mesh/Cube", "primitive:Cube", "editor:Checkerboard" })
            {
                Assert.True(AssetPath.IsPseudoPath(reference));
                Assert.Equal(reference, AssetPath.Resolve(reference));
                Assert.Equal(reference, AssetPath.MakeRelative(reference));
                Assert.Equal(reference, AssetDatabase.ToGuidRef(reference));
            }
        }
        finally
        {
            AssetPath.Root = string.Empty;
        }
    }

    // ----- Meshes ----------------------------------------------------------------------------------

    [Fact]
    public void Meshes_LoadThroughTheModelImporter_SharedWithTheirLegacyAlias()
    {
        Install();

        Model model = ModelImporter.Load("builtin:Mesh/Capsule?radius=0.25&height=3");

        Assert.Same(model, ModelImporter.Load("primitive:Capsule?height=3&radius=0.25"));
        Assert.Same(model, BuiltinAssets.LoadModel("builtin:mesh/capsule?radius=0.25&height=3"));
        Assert.Same(model, ModelImporter.RequestAsync("builtin:Mesh/Capsule?radius=0.25&height=3"));
        Assert.Equal(new Vector3(0.5f, 3, 0.5f), model.LocalBounds.Max - model.LocalBounds.Min);
    }

    [Fact]
    public void Meshes_UnknownNamesThrowOnLoadAndLogOnceWhenRequested()
    {
        Install();
        using RecordingLogSink log = RecordingLogSink.Capture();

        Assert.Throws<ArgumentException>(() => BuiltinAssets.LoadModel("builtin:Mesh/Teapot"));
        Assert.Throws<ArgumentException>(() => BuiltinAssets.LoadModel("builtin:Texture/Grid"));
        Assert.Null(ModelImporter.RequestAsync("builtin:Mesh/Teapot"));
        Assert.Null(ModelImporter.RequestAsync("builtin:Mesh/Teapot"));

        Assert.Single(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Teapot", StringComparison.Ordinal));
    }

    [Fact]
    public void MeshReference_WritesTheCanonicalForm()
    {
        PrimitiveSpec spec = PrimitiveSpec.For(PrimitiveShape.Cylinder).With(PrimitiveParameter.Segments, 12);

        Assert.Equal("builtin:Mesh/Cylinder?segments=12", BuiltinAssets.MeshReference(spec));
        Assert.True(BuiltinAssets.TryGetPrimitive("builtin:Mesh/Cylinder?segments=12", out PrimitiveSpec back));
        Assert.Equal(spec, back);
        Assert.False(BuiltinAssets.TryGetPrimitive("builtin:Material/Grid", out _));
    }

    // ----- Textures --------------------------------------------------------------------------------

    [Fact]
    public void Textures_AreSharedAndLoadThroughTexture2DLoad()
    {
        Install();

        Texture2D grid = BuiltinAssets.LoadTexture("builtin:Texture/Grid");

        Assert.Same(grid, Texture2D.Load("builtin:Texture/Grid"));
        Assert.Same(grid, Texture2D.Load("builtin:texture/GRID"));
        Assert.NotSame(grid, BuiltinAssets.LoadTexture("builtin:Texture/Checker"));
        Assert.Equal((512u, 512u), (grid.Width, grid.Height));
    }

    [Fact]
    public void Textures_DisposedByMistakeAreRebuilt()
    {
        RecordingGraphicsDevice device = Install();
        Texture2D white = BuiltinAssets.LoadTexture("builtin:Texture/White");

        white.Dispose();
        Texture2D again = BuiltinAssets.LoadTexture("builtin:Texture/White");

        Assert.NotSame(white, again);
        Assert.Contains(again.Handle.Id, device.LiveTextures);
    }

    [Fact]
    public void Textures_AreRebuiltForANewDevice()
    {
        Install();
        Texture2D before = BuiltinAssets.LoadTexture("builtin:Texture/Black");

        RecordingGraphicsDevice next = Install();
        Texture2D after = BuiltinAssets.LoadTexture("builtin:Texture/Black");

        Assert.NotSame(before, after);
        Assert.Contains(after.Handle.Id, next.LiveTextures);
    }

    [Fact]
    public void Textures_RejectOtherKindsAndUnknownNames()
    {
        Install();

        Assert.Throws<ArgumentException>(() => BuiltinAssets.LoadTexture("builtin:Material/Grid"));
        Assert.Throws<ArgumentException>(() => BuiltinAssets.LoadTexture("builtin:Texture/Plaid"));
        Assert.Throws<ArgumentException>(() => BuiltinAssets.CreateImage("builtin:Mesh/Cube"));
    }

    [Fact]
    public void CreateImage_GeneratesEachTexture()
    {
        foreach (BuiltinAsset texture in BuiltinAssets.OfKind(BuiltinAssetKind.Texture))
        {
            Image image = BuiltinAssets.CreateImage(texture.Reference);
            Assert.True(image.Width > 0 && image.Height > 0, texture.Name);
        }

        Assert.Equal(new byte[] { 128, 128, 255, 255 }, BuiltinAssets.CreateImage("builtin:Texture/FlatNormal").Pixels[..4]);
    }

    // ----- Materials -------------------------------------------------------------------------------

    [Fact]
    public void Materials_AreSharedAndUseSharedTextures()
    {
        Install();

        Material grid = Material.Load("builtin:Material/Grid");

        Assert.Same(grid, BuiltinAssets.LoadMaterial("builtin:material/grid"));
        Assert.True(grid.IsBuiltin);
        Assert.Same(BuiltinAssets.LoadTexture("builtin:Texture/Grid"), grid.Texture);
        Assert.Equal("builtin:Texture/Grid", grid.TexturePath);
        Assert.True(grid.AutoTile);
        Assert.Equal(new Vector2(0.125f), grid.Tiling);
        Assert.Null(Material.Load("builtin:Material/Default").Texture);
    }

    [Fact]
    public void Materials_TheLegacyCheckerboardIsTheCheckerMaterial()
    {
        Install();

        Material checker = Material.Load("editor:Checkerboard");

        Assert.Same(BuiltinAssets.LoadMaterial("builtin:Material/Checker"), checker);
        Assert.Equal(new Vector2(0.25f), checker.Tiling);
    }

    [Fact]
    public void Materials_AnUnknownBuiltinLogsOnceAndFallsBackToDefault()
    {
        Install();
        using RecordingLogSink log = RecordingLogSink.Capture();

        Material first = Material.Load("builtin:Material/Velvet");
        Material second = Material.Load("builtin:Material/Velvet");

        Assert.Same(first, second);
        Assert.Null(first.Texture);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Velvet", StringComparison.Ordinal));
    }

    [Fact]
    public void Materials_ReplacingASharedTextureDoesNotDisposeIt()
    {
        RecordingGraphicsDevice device = Install();
        Material copy = BuiltinAssets.CreateMaterial("builtin:Material/Checker");
        Texture2D shared = BuiltinAssets.LoadTexture("builtin:Texture/Checker");
        Assert.Same(shared, copy.Texture);

        copy.SetTexture(null);

        Assert.Contains(shared.Handle.Id, device.LiveTextures);
        Assert.Same(shared, BuiltinAssets.LoadTexture("builtin:Texture/Checker"));
    }

    [Fact]
    public void CreateMaterial_IsAnEditableCopyThatSavesWithBuiltinTextures()
    {
        Install();
        using var dir = new TempDir();
        AssetPath.Root = dir.Path;
        try
        {
            Material copy = BuiltinAssets.CreateMaterial("builtin:Material/Grid");
            Assert.NotSame(BuiltinAssets.LoadMaterial("builtin:Material/Grid"), copy);

            copy.Color = new Vector4(1, 0, 0, 1);
            string path = Path.Combine(dir.Path, "RedGrid.sptmat");
            copy.Save(path);

            Assert.False(copy.IsBuiltin);
            JsonNode saved = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.Equal("builtin:Texture/Grid", saved["TexturePath"]!.GetValue<string>());
            Assert.Equal(new Vector4(1, 1, 1, 1), BuiltinAssets.LoadMaterial("builtin:Material/Grid").Color);
        }
        finally
        {
            AssetPath.Root = string.Empty;
        }
    }

    // ----- Copies into the project ------------------------------------------------------------------

    [Fact]
    public void Export_WritesEditableFilesNamedAfterTheAsset()
    {
        Install();
        using var dir = new TempDir();

        string mesh = BuiltinAssets.Export("builtin:Mesh/Capsule?radius=0.3&height=1.7", dir.Path);
        string texture = BuiltinAssets.Export("builtin:Texture/Grid", dir.Path);
        string material = BuiltinAssets.Export("editor:Checkerboard", dir.Path);

        Assert.Equal(Path.Combine(dir.Path, "Capsule.obj"), mesh);
        Assert.Equal(Path.Combine(dir.Path, "Grid.png"), texture);
        Assert.Equal(Path.Combine(dir.Path, "Checker.sptmat"), material);

        // The mesh bakes in its parameters; the texture is the generated image; the material keeps its settings.
        Assert.Contains("o Capsule", File.ReadAllText(mesh), StringComparison.Ordinal);
        Assert.Equal(1.7f, ObjHeight(mesh), 3);
        Assert.Equal(BuiltinAssets.CreateImage("builtin:Texture/Grid").Pixels, Image.FromFile(texture).Pixels);
        JsonNode saved = JsonNode.Parse(File.ReadAllText(material))!;
        Assert.Equal("builtin:Texture/Checker", saved["TexturePath"]!.GetValue<string>());
        Assert.True(saved["AutoTile"]!.GetValue<bool>());
    }

    [Fact]
    public void Export_NumbersCopiesInsteadOfOverwriting()
    {
        Install();
        using var dir = new TempDir();

        string first = BuiltinAssets.Export("builtin:Texture/White", dir.Path);
        string second = BuiltinAssets.Export("builtin:Texture/White", dir.Path);
        string nested = BuiltinAssets.Export("builtin:Mesh/Cube", Path.Combine(dir.Path, "Shapes", "Basic"));

        Assert.Equal(Path.Combine(dir.Path, "White 1.png"), second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(nested));
        Assert.Throws<ArgumentException>(() => BuiltinAssets.Export("builtin:Mesh/Teapot", dir.Path));
    }

    [Fact]
    public void ModelInstantiator_TurnsABuiltinMeshIntoOneEntity()
    {
        var scene = new Scene();
        Entity parent = scene.Instantiate("Parent");

        Entity? created = ModelInstantiator.Instantiate(scene, "primitive:Cylinder?segments=8", parent);

        Entity shape = Assert.NotNull(created);
        Assert.Equal("Cylinder", shape.Name);
        Assert.Equal(parent, shape.Parent);
        Assert.Equal("builtin:Mesh/Cylinder?segments=8", shape.GetComponent<MeshRenderer>().ModelPath);
        Assert.Null(ModelInstantiator.Instantiate(scene, "builtin:Mesh/Teapot"));
    }

    private static float ObjHeight(string objPath)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (string line in File.ReadLines(objPath).Where(l => l.StartsWith("v ", StringComparison.Ordinal)))
        {
            float y = float.Parse(line.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture);
            min = Math.Min(min, y);
            max = Math.Max(max, y);
        }

        return max - min;
    }

    // ----- Scenes and rendering --------------------------------------------------------------------

    [Fact]
    public void MeshRenderers_StoreTheCurrentFormOfLegacyReferences()
    {
        const string json = """
        {
          "Entities": [
            {
              "Tag": { "Name": "Ground", "Enabled": true },
              "Transform": { "Position": [0, 0, 0], "Rotation": [0, 0, 0], "Scale": [1, 1, 1], "Enabled": true },
              "MeshRenderer": { "ModelPath": "primitive:Plane", "MaterialPath": "editor:Checkerboard", "Enabled": true }
            }
          ]
        }
        """;

        var scene = new Scene();
        Assert.True(new SceneSerializer(scene).DeserializeFromString(json));
        MeshRenderer mesh = scene.View<MeshRenderer>().Select(e => e.GetComponent<MeshRenderer>()).Single();
        Assert.Equal(("builtin:Mesh/Plane", "builtin:Material/Checker"), (mesh.ModelPath, mesh.MaterialPath));

        string resaved = new SceneSerializer(scene).SerializeToString();
        Assert.Contains("builtin:Mesh/Plane", resaved, StringComparison.Ordinal);
        Assert.DoesNotContain("primitive:", resaved, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderSystem_DrawsBuiltinMeshesWithBuiltinMaterials()
    {
        RecordingGraphicsDevice device = Install();
        Renderer3D.Init();
        Renderer.SetViewport(0, 0, 320, 180);
        var scene = new Scene();
        Entity capsule = scene.Instantiate("Capsule");
        capsule.AddComponent(new MeshRenderer { ModelPath = "builtin:Mesh/Capsule?segments=12", MaterialPath = "builtin:Material/Grid" });

        RenderSystem.Render(scene, Matrix4x4.CreateScale(0.25f), new Vector3(0, 0, 5));

        var mesh = capsule.GetComponent<MeshRenderer>();
        Assert.Same(PrimitiveModelFactory.Get("Capsule?segments=12"), mesh.Model);
        Assert.Same(BuiltinAssets.LoadMaterial("builtin:Material/Grid"), mesh.Material);
        int indices = PrimitiveSpec.Parse("Capsule?segments=12").Build().Indices.Length;
        Assert.Contains(device.Draws, d => d.Count == indices);
    }
}
