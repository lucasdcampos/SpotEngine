using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Spot.DebugUI.UI;
using Spot.Engine.Assets;
using Spot.Engine;
using Spot.Engine.Scenes;
using Spot.Engine.Graphics;
using Spot.Tests;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers what dragging an asset into the scene builds: the editor's viewport, Hierarchy and "Add to Scene"
/// all go through <see cref="AssetSpawner"/>.
/// </summary>
public class AssetSpawnerTests
{
    [Theory]
    [InlineData("Enemy.sptprefab", AssetSpawnKind.Prefab)]
    [InlineData("Robot.FBX", AssetSpawnKind.Model)]
    [InlineData("crate.glb", AssetSpawnKind.Model)]
    [InlineData("hero.png", AssetSpawnKind.Sprite)]
    [InlineData("tiles.JPG", AssetSpawnKind.Sprite)]
    [InlineData("jump.ogg", AssetSpawnKind.AudioSource)]
    [InlineData("Hud.sptui", AssetSpawnKind.UICanvas)]
    [InlineData("Rock.sptmat", AssetSpawnKind.None)]
    [InlineData("Player.cs", AssetSpawnKind.None)]
    [InlineData("Main.sptscene", AssetSpawnKind.None)]
    [InlineData("", AssetSpawnKind.None)]
    public void KindOf_ClassifiesByExtension(string path, AssetSpawnKind expected)
    {
        Assert.Equal(expected, AssetSpawner.KindOf(path));
    }

    [Fact]
    public void KindOf_ClassifiesBuiltins()
    {
        Assert.Equal(AssetSpawnKind.Model, AssetSpawner.KindOf(BuiltinAssets.OfKind(BuiltinAssetKind.Mesh).First().Reference));
        Assert.Equal(AssetSpawnKind.Sprite, AssetSpawner.KindOf(BuiltinAssets.OfKind(BuiltinAssetKind.Texture).First().Reference));
        Assert.Equal(AssetSpawnKind.None, AssetSpawner.KindOf(BuiltinAssets.OfKind(BuiltinAssetKind.Material).First().Reference));
    }

    [Fact]
    public void Spawn_PrefabInstantiatesItsTreeLinkedToThePrefab()
    {
        using var dir = new TempDir();
        var source = new Scene();
        Entity turret = source.Instantiate("Turret");
        source.Instantiate("Barrel").SetParent(turret);
        string path = Path.Combine(dir.Path, "Turret.sptprefab");
        File.WriteAllText(path, Prefab.Serialize(turret));

        var scene = new Scene();
        Entity? root = AssetSpawner.Spawn(scene, path);

        Assert.NotNull(root);
        Assert.Equal("Turret", root!.Value.Name);
        Assert.Equal("Barrel", Assert.Single(root.Value.Children.ToList()).Name);
        Assert.False(string.IsNullOrEmpty(root.Value.GetComponent<PrefabComponent>().PrefabRef));
    }

    [Fact]
    public void Spawn_BuiltinMeshBecomesAShapeEntity()
    {
        BuiltinAsset cube = BuiltinAssets.OfKind(BuiltinAssetKind.Mesh).First();
        var scene = new Scene();

        Entity? root = AssetSpawner.Spawn(scene, cube.Reference);

        Assert.NotNull(root);
        Assert.Equal(cube.Name, root!.Value.Name);
        Assert.Equal(cube.Reference, root.Value.GetComponent<MeshComponent>().ModelPath);
    }

    [Fact]
    public void Spawn_ImageBecomesASpriteKeepingThePictureProportions()
    {
        Renderer.Init(new RecordingGraphicsDevice());
        using var dir = new TempDir();
        string image = Path.Combine(dir.Path, "banner.png");
        File.WriteAllBytes(image, TestMedia.Png(4, 2, new byte[4 * 2 * 4]));
        string cooked = Path.Combine(dir.Path, "banner.spttex");
        File.WriteAllBytes(cooked, SpTex.Write(4, 2, new byte[4 * 2 * 4], pointFilter: false));

        Func<string, string?>? previous = AssetPath.ContentResolver;
        try
        {
            AssetPath.ContentResolver = _ => cooked;

            var scene = new Scene();
            Entity? sprite = AssetSpawner.Spawn(scene, image);

            Assert.NotNull(sprite);
            Assert.Equal("banner", sprite!.Value.Name);
            var component = sprite.Value.GetComponent<Sprite2DComponent>();
            Assert.Equal(AssetDatabase.ToGuidRef(image), component.TexturePath);
            Assert.NotNull(component.Texture);

            // A 4x2 picture on the unit quad is stretched to twice as wide as tall.
            Assert.Equal(new Vector3(2, 1, 1), sprite.Value.GetComponent<TransformComponent>().Scale);
        }
        finally
        {
            AssetPath.ContentResolver = previous;
        }
    }

    [Fact]
    public void Spawn_UnloadableImageStillCreatesTheSprite()
    {
        // Never crash: an image that cannot be loaded logs and leaves a sprite pointing at it.
        using var dir = new TempDir();
        string image = Path.Combine(dir.Path, "broken.png");
        File.WriteAllBytes(image, new byte[] { 1, 2, 3 });

        var scene = new Scene();
        Entity? sprite = AssetSpawner.Spawn(scene, image);

        Assert.NotNull(sprite);
        Assert.NotNull(sprite!.Value.GetComponent<Sprite2DComponent>().TexturePath);
        Assert.Equal(Vector3.One, sprite.Value.GetComponent<TransformComponent>().Scale);
    }

    [Fact]
    public void Spawn_AudioClipBecomesAnAudioSource()
    {
        using var dir = new TempDir();
        string clip = Path.Combine(dir.Path, "jump.wav");
        File.WriteAllBytes(clip, TestMedia.Wav16(new short[] { 1, -1 }, 1, 8000));

        Entity? entity = AssetSpawner.Spawn(new Scene(), clip);

        Assert.NotNull(entity);
        Assert.Equal("jump", entity!.Value.Name);
        Assert.Equal(AssetDatabase.ToGuidRef(clip), entity.Value.GetComponent<AudioSourceComponent>().ClipPath);
    }

    [Fact]
    public void Spawn_UIDocumentBecomesACanvas()
    {
        using var dir = new TempDir();
        string document = Path.Combine(dir.Path, "Hud.sptui");
        File.WriteAllText(document, "{}");

        Entity? entity = AssetSpawner.Spawn(new Scene(), document);

        Assert.NotNull(entity);
        Assert.Equal("Hud", entity!.Value.Name);
        Assert.Equal(AssetDatabase.ToGuidRef(document), entity.Value.GetComponent<UICanvasComponent>().DocumentRef);
    }

    [Fact]
    public void Spawn_UnderAParentAddsAChild()
    {
        var scene = new Scene();
        Entity parent = scene.Instantiate("Holder");

        Entity? child = AssetSpawner.Spawn(scene, BuiltinAssets.OfKind(BuiltinAssetKind.Mesh).First().Reference, parent);

        Assert.NotNull(child);
        Assert.Equal("Holder", child!.Value.Parent?.Name);
    }

    [Fact]
    public void SpawnAll_SkipsAssetsThatStandForNoEntity()
    {
        var scene = new Scene();
        string[] paths =
        {
            BuiltinAssets.OfKind(BuiltinAssetKind.Mesh).First().Reference,
            BuiltinAssets.OfKind(BuiltinAssetKind.Material).First().Reference,
            "Player.cs",
            Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".sptprefab"),
        };

        var spawned = AssetSpawner.SpawnAll(scene, paths);

        Assert.Single(spawned);
        Assert.Single(scene.View<TransformComponent>());
    }
}
