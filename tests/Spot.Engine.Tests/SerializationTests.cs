using System;
using System.IO;
using System.Numerics;
using Spot.Core;
using Spot.Physics;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Engine.Tests;

// A probe script that scene deserialization resolves by class name via reflection across loaded
// assemblies. Kept top-level and public so Activator can construct it.
public sealed class SerializationProbeBehaviour : EntityBehaviour
{
}

public class SerializationTests
{
    [Fact]
    public void Scene_RoundTripsEntityData()
    {
        var scene = new Scene();
        var hero = scene.Instantiate("Hero");
        var t = hero.GetComponent<Transform>();
        t.Position = new Vector3(1, 2, 3);
        t.Rotation = new Vector3(0, 45, 0);
        t.Scale = new Vector3(2, 2, 2);
        hero.AddComponent(new CameraComponent { Primary = true, ZoomLevel = 7f, ProjectionType = SceneCameraProjection.Perspective });
        hero.AddComponent(new PhysicsBody2DComponent { GravityScale = 0.5f, IsDynamic = false, Velocity = new Vector2(1, 2) });
        hero.AddComponent(new BoxCollider2DComponent { Size = new Vector2(3, 4), Offset = new Vector2(1, 1) });
        hero.AddComponent(new LightComponent { Type = LightType.Directional, Intensity = 2f, AmbientIntensity = 0.1f, Color = new Vector3(1, 0, 0) });

        var sidekick = scene.Instantiate("Sidekick");
        sidekick.SetParent(hero);

        string json = new SceneSerializer(scene).SerializeToString();

        var loaded = new Scene();
        Assert.True(new SceneSerializer(loaded).DeserializeFromString(json));

        var loadedHero = FindByName(loaded, "Hero");
        var ht = loadedHero.GetComponent<Transform>();
        Assert.Equal(new Vector3(1, 2, 3), ht.Position);
        Assert.Equal(new Vector3(0, 45, 0), ht.Rotation);
        Assert.Equal(new Vector3(2, 2, 2), ht.Scale);

        var cam = loadedHero.GetComponent<CameraComponent>();
        Assert.Equal(SceneCameraProjection.Perspective, cam.ProjectionType);
        Assert.Equal(7f, cam.ZoomLevel);

        var body = loadedHero.GetComponent<PhysicsBody2DComponent>();
        Assert.False(body.IsDynamic);
        Assert.Equal(0.5f, body.GravityScale);
        Assert.Equal(new Vector2(1, 2), body.Velocity);

        var collider = loadedHero.GetComponent<BoxCollider2DComponent>();
        Assert.Equal(new Vector2(3, 4), collider.Size);
        Assert.Equal(new Vector2(1, 1), collider.Offset);

        var light = loadedHero.GetComponent<LightComponent>();
        Assert.Equal(2f, light.Intensity);
        Assert.Equal(new Vector3(1, 0, 0), light.Color);
        Assert.Equal(LightType.Directional, light.Type);

        // Parent/child hierarchy is preserved through the nested Children shape.
        var loadedSidekick = FindByName(loaded, "Sidekick");
        Assert.True(loadedSidekick.Parent.HasValue);
        Assert.Equal("Hero", loadedSidekick.Parent!.Value.Name);
    }

    [Fact]
    public void Scene_ResolvesScriptByClassName()
    {
        var scene = new Scene();
        var e = scene.Instantiate("Scripted");
        e.AddComponent(new ScriptComponent { ClassNames = { nameof(SerializationProbeBehaviour) } });

        string json = new SceneSerializer(scene).SerializeToString();

        var loaded = new Scene();
        Assert.True(new SceneSerializer(loaded).DeserializeFromString(json));

        var scripted = FindByName(loaded, "Scripted");
        var comp = scripted.GetComponent<ScriptComponent>();
        Assert.Contains(nameof(SerializationProbeBehaviour), comp.ClassNames);
        Assert.Single(comp.Scripts);
        Assert.IsType<SerializationProbeBehaviour>(comp.Scripts[0]);
    }

    [Fact]
    public void Deserialize_MissingAssetDegradesGracefully()
    {
        // A sprite pointing at a texture that does not exist must not crash deserialization: the loader
        // catches and logs, and the entity is still created (the engine's "log and continue" rule).
        string json = """
        {
          "Entities": [
            { "Tag": { "Name": "Ghost" }, "Sprite": { "Color": [1, 1, 1, 1], "TexturePath": "does/not/exist.png" } }
          ]
        }
        """;

        var loaded = new Scene();
        bool ok = false;
        var exception = Record.Exception(() => ok = new SceneSerializer(loaded).DeserializeFromString(json));

        Assert.Null(exception);
        Assert.True(ok);
        Assert.NotNull(FindByNameOrNull(loaded, "Ghost"));
    }

    [Fact]
    public void Deserialize_InvalidJsonReturnsFalseWithoutThrowing()
    {
        var loaded = new Scene();
        bool ok = true;
        var exception = Record.Exception(() => ok = new SceneSerializer(loaded).DeserializeFromString("{ not valid json"));

        Assert.Null(exception);
        Assert.False(ok);
    }


    private static Entity FindByName(Scene scene, string name) =>
        FindByNameOrNull(scene, name) ?? throw new InvalidOperationException($"Entity '{name}' was not found.");

    private static Entity? FindByNameOrNull(Scene scene, string name)
    {
        foreach (var e in scene.View<TagComponent>())
        {
            if (e.Name == name)
            {
                return e;
            }
        }

        return null;
    }
}
