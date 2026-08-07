using System;
using System.Linq;
using System.Numerics;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Engine.Tests;

public class PrefabTests
{
    [Fact]
    public void Serialize_RoundTripsSubtreeIntoScene()
    {
        var source = new Scene();
        var root = source.Instantiate("Turret");
        var t = root.GetComponent<TransformComponent>();
        t.Position = new Vector3(5, 0, -2);
        t.Scale = new Vector3(2, 2, 2);
        root.AddComponent(new LightComponent { Type = LightType.Point, Intensity = 3f });

        var barrel = source.Instantiate("Barrel");
        barrel.SetParent(root);
        barrel.GetComponent<TransformComponent>().Position = new Vector3(0, 1, 0);

        string prefabJson = Prefab.Serialize(root);

        // Instantiate the prefab into a different scene and verify the subtree is faithfully reconstructed.
        var target = new Scene();
        Entity? instance = Prefab.InstantiateInto(target, prefabJson, null);

        Assert.NotNull(instance);
        Assert.Equal("Turret", instance!.Value.Name);
        Assert.Equal(new Vector3(5, 0, -2), instance.Value.GetComponent<TransformComponent>().Position);
        Assert.Equal(new Vector3(2, 2, 2), instance.Value.GetComponent<TransformComponent>().Scale);

        var light = instance.Value.GetComponent<LightComponent>();
        Assert.Equal(LightType.Point, light.Type);
        Assert.Equal(3f, light.Intensity);

        var child = Assert.Single(instance.Value.Children.ToList());
        Assert.Equal("Barrel", child.Name);
        Assert.Equal(new Vector3(0, 1, 0), child.GetComponent<TransformComponent>().Position);
        Assert.True(child.Parent.HasValue);
        Assert.Equal("Turret", child.Parent!.Value.Name);
    }

    [Fact]
    public void Serialize_DropsPrefabInstanceLink()
    {
        // A source entity that is itself an instance must not carry that link into the new prefab definition.
        var scene = new Scene();
        var entity = scene.Instantiate("Crate");
        entity.AddComponent(new PrefabComponent { PrefabRef = "guid:deadbeef" });

        string prefabJson = Prefab.Serialize(entity);

        var target = new Scene();
        Entity? instance = Prefab.InstantiateInto(target, prefabJson, null);

        Assert.NotNull(instance);
        Assert.False(instance!.Value.HasComponent<PrefabComponent>());
    }

    [Fact]
    public void InstantiateInto_ParentsUnderGivenEntity()
    {
        var scene = new Scene();
        var mount = scene.Instantiate("Mount");

        string prefabJson = Prefab.Serialize(new Scene().Instantiate("Widget"));
        Entity? instance = Prefab.InstantiateInto(scene, prefabJson, mount);

        Assert.NotNull(instance);
        Assert.True(instance!.Value.Parent.HasValue);
        Assert.Equal("Mount", instance.Value.Parent!.Value.Name);
    }

    [Fact]
    public void InstantiateInto_InvalidJsonReturnsNullWithoutThrowing()
    {
        var scene = new Scene();

        Entity? result = null;
        var empty = Record.Exception(() => result = Prefab.InstantiateInto(scene, "", null));
        Assert.Null(empty);
        Assert.Null(result);

        var malformed = Record.Exception(() => result = Prefab.InstantiateInto(scene, "{ not json", null));
        Assert.Null(malformed);
        Assert.Null(result);

        var noRoot = Record.Exception(() => result = Prefab.InstantiateInto(scene, "{ \"Nope\": 1 }", null));
        Assert.Null(noRoot);
        Assert.Null(result);
    }
}
