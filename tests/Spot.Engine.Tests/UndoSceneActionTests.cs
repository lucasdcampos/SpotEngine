using System.Reflection;
using Spot.DebugUI.Undo;
using Spot.Engine.Scenes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the scene-targeted actions, whose defining property is that they resolve their target when
/// applied instead of capturing it. The re-hydration tests are the point: they are what a
/// closure-capturing action would silently fail.
/// </summary>
public class UndoSceneActionTests
{
    private static MemberAccessor IntensityAccessor() => MemberAccessor.FromProperty(
        typeof(LightComponent).GetProperty(nameof(LightComponent.Intensity))!);

    private static ComponentValueAction IntensityAction(
        Scene scene, string entityId, float before, float after) =>
        new("Set Intensity", scene, entityId, typeof(LightComponent), IntensityAccessor(), before, after);

    [Fact]
    public void AComponentValueActionRoundTripsOnTheLiveScene()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("Sun");
        entity.AddComponent(new LightComponent { Intensity = 1.0f });
        string id = entity.EnsurePersistentId();

        var action = IntensityAction(scene, id, 1.0f, 5.0f);

        action.Redo();
        Assert.Equal(5.0f, entity.GetComponent<LightComponent>().Intensity);

        action.Undo();
        Assert.Equal(1.0f, entity.GetComponent<LightComponent>().Intensity);
    }

    [Fact]
    public void AComponentValueActionStillAppliesAfterTheSceneIsReHydrated()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("Sun");
        entity.AddComponent(new LightComponent { Intensity = 1.0f });

        // Serializing assigns the stable ids and is what the editor's snapshot does.
        string json = new SceneSerializer(scene).SerializeToString();
        string id = entity.EnsurePersistentId();
        Assert.False(string.IsNullOrEmpty(id));

        var action = IntensityAction(scene, id, 1.0f, 5.0f);
        action.Redo();

        // Hold the actual component object, which is what a closure-capturing action would have kept.
        LightComponent captured = entity.GetComponent<LightComponent>();

        // Simulate leaving play mode: the scene is cleared and deserialized into the same instance, so
        // every component object is replaced and every runtime int id is re-minted.
        scene.Clear();
        Assert.True(new SceneSerializer(scene).DeserializeFromString(json));

        Entity? restored = scene.EntityByPersistentId(id);
        Assert.NotNull(restored);

        // The object the closure would have held is now detached from the scene — writing to it would
        // change nothing a user can see. This is the failure the late-resolving action avoids.
        Assert.NotSame(captured, restored!.Value.GetComponent<LightComponent>());

        // The action targets the stable id, so it still finds its mark on the new instance.
        action.Redo();
        Assert.Equal(5.0f, restored.Value.GetComponent<LightComponent>().Intensity);

        action.Undo();
        Assert.Equal(1.0f, restored.Value.GetComponent<LightComponent>().Intensity);
    }

    [Fact]
    public void AnActionWhoseEntityIsGoneLogsAndDoesNothing()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("Sun");
        entity.AddComponent(new LightComponent());
        string id = entity.EnsurePersistentId();

        var action = IntensityAction(scene, id, 1.0f, 5.0f);

        scene.Destroy(entity);
        scene.FlushDestroyed();

        // Must not throw: the user may have deleted the entity and undone past this point.
        action.Undo();
        action.Redo();
    }

    [Fact]
    public void AnActionWhoseComponentIsGoneLogsAndDoesNothing()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("Sun");
        entity.AddComponent(new LightComponent());
        string id = entity.EnsurePersistentId();

        var action = IntensityAction(scene, id, 1.0f, 5.0f);
        entity.RemoveComponent(typeof(LightComponent));

        action.Undo();
        action.Redo();
    }

    [Fact]
    public void EntityByPersistentIdFindsOnlyLiveEntitiesAndIgnoresBlankIds()
    {
        var scene = new Scene();
        Entity a = scene.Instantiate("A");
        Entity b = scene.Instantiate("B");
        string idA = a.EnsurePersistentId();
        string idB = b.EnsurePersistentId();

        Assert.Equal(a, scene.EntityByPersistentId(idA));
        Assert.Equal(b, scene.EntityByPersistentId(idB));
        Assert.Null(scene.EntityByPersistentId("not-a-real-id"));
        Assert.Null(scene.EntityByPersistentId(null));
        Assert.Null(scene.EntityByPersistentId(string.Empty));

        scene.Destroy(a);
        scene.FlushDestroyed();
        Assert.Null(scene.EntityByPersistentId(idA));
    }

    [Fact]
    public void ReadEntityPreservesStableIdsWhenAsked()
    {
        var source = new Scene();
        Entity entity = source.Instantiate("Thing");
        entity.AddComponent(new LightComponent { Intensity = 3.0f });
        string id = entity.EnsurePersistentId();

        var json = SceneSerializer.WriteEntity(entity);

        // The default keeps prefab instancing safe by minting a new identity...
        var fresh = new Scene();
        Entity instanced = SceneSerializer.ReadEntity(fresh, json, null);
        Assert.NotEqual(id, instanced.PersistentId);

        // ...while undo needs the original identity back, or every older history entry keyed to this
        // entity, and every Entity-typed field pointing at it, would be orphaned.
        var restoredScene = new Scene();
        Entity restored = SceneSerializer.ReadEntity(restoredScene, json, null, preserveIds: true);
        Assert.Equal(id, restored.PersistentId);
        Assert.Equal(3.0f, restored.GetComponent<LightComponent>().Intensity);
    }

    [Fact]
    public void TheCatchAllSnapshotActionRoundTripsAWholeSceneExactly()
    {
        var scene = new Scene();
        Entity a = scene.Instantiate("Parent");
        a.AddComponent(new LightComponent { Intensity = 1.0f });
        Entity b = scene.Instantiate("Child");
        b.SetParent(a);

        var serializer = new SceneSerializer(scene);
        string before = serializer.SerializeToString();

        // An un-migrated mutation site: a change nothing recorded a precise action for.
        a.GetComponent<LightComponent>().Intensity = 9.0f;
        scene.Instantiate("Added Behind The History");
        string after = serializer.SerializeToString();
        Assert.NotEqual(before, after);

        var action = new DocumentSnapshotAction("Scene Change", null, before, after, json =>
        {
            scene.Clear();
            new SceneSerializer(scene).DeserializeFromString(json);
        });

        // Full-scene JSON equality is a deliberately strict oracle: it catches a restore that gets the
        // entity tree, a component value, or a stable id even slightly wrong.
        action.Undo();
        Assert.Equal(before, new SceneSerializer(scene).SerializeToString());

        action.Redo();
        Assert.Equal(after, new SceneSerializer(scene).SerializeToString());

        action.Undo();
        Assert.Equal(before, new SceneSerializer(scene).SerializeToString());

        // The snapshot pair is what the history's byte budget accounts for.
        Assert.True(action.ApproxSizeBytes > before.Length);
    }

    [Fact]
    public void MemberAccessorReadsAndWritesPropertiesAndFields()
    {
        var light = new LightComponent { Intensity = 2.0f };
        MemberAccessor property = IntensityAccessor();

        Assert.Equal("Intensity", property.Name);
        Assert.Equal(typeof(float), property.MemberType);
        Assert.Equal(2.0f, property.Get(light));
        property.Set(light, 8.0f);
        Assert.Equal(8.0f, light.Intensity);

        var target = new FieldHolder { Count = 1 };
        FieldInfo field = typeof(FieldHolder).GetField(nameof(FieldHolder.Count))!;
        MemberAccessor accessor = MemberAccessor.FromField(field);

        Assert.Equal(1, accessor.Get(target));
        accessor.Set(target, 42);
        Assert.Equal(42, target.Count);

        // Two accessors to the same member compare equal, so the tracker can confirm a commit belongs
        // to the edit it opened.
        Assert.Equal(accessor, MemberAccessor.FromField(field));
        Assert.NotEqual(accessor, property);
    }

    private sealed class FieldHolder
    {
        public int Count;
    }
}
