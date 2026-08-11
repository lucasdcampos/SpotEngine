using Spot.Physics;
using Spot.Scenes;

namespace Spot.Engine.Tests;

public class EntityQueryTests
{
    [Fact]
    public void Find_ReturnsFirstEntityWithMatchingName()
    {
        var scene = new Scene();
        scene.Instantiate("Alpha");
        var beta = scene.Instantiate("Beta");

        Entity? found = scene.Find("Beta");

        Assert.NotNull(found);
        Assert.Equal(beta.Id, found!.Value.Id);
        Assert.Null(scene.Find("Missing"));
    }

    [Fact]
    public void FindByTag_And_FindAllByTag_MatchOnTag()
    {
        var scene = new Scene();
        var a = scene.Instantiate("A");
        var b = scene.Instantiate("B");
        var c = scene.Instantiate("C");
        a.Tag = "Enemy";
        b.Tag = "Enemy";
        c.Tag = "Player";

        Entity? first = scene.FindByTag("Enemy");
        Assert.NotNull(first);
        Assert.Contains(first!.Value.Id, new[] { a.Id, b.Id });

        var enemies = scene.FindAllByTag("Enemy");
        Assert.Equal(2, enemies.Count);

        Assert.True(c.CompareTag("Player"));
        Assert.False(c.CompareTag("Enemy"));
        Assert.Null(scene.FindByTag("Boss"));
    }

    [Fact]
    public void GetComponentInChildren_FindsOnSelfAndDescendants()
    {
        var scene = new Scene();
        var root = scene.Instantiate("root");
        var child = scene.Instantiate("child");
        var grandchild = scene.Instantiate("grandchild");
        child.SetParent(root);
        grandchild.SetParent(child);
        grandchild.AddComponent(new SphereCollider3DComponent());

        SphereCollider3DComponent? found = root.GetComponentInChildren<SphereCollider3DComponent>();
        Assert.NotNull(found);

        // A component on the entity itself is returned before descending.
        root.AddComponent(new BoxCollider3DComponent());
        Assert.NotNull(root.GetComponentInChildren<BoxCollider3DComponent>());

        Assert.Null(root.GetComponentInChildren<CapsuleCollider3DComponent>());
    }

    [Fact]
    public void GetComponentInParent_WalksUpAncestors()
    {
        var scene = new Scene();
        var root = scene.Instantiate("root");
        var child = scene.Instantiate("child");
        child.SetParent(root);
        root.AddComponent(new BoxCollider3DComponent());

        BoxCollider3DComponent? found = child.GetComponentInParent<BoxCollider3DComponent>();
        Assert.NotNull(found);

        Assert.Null(child.GetComponentInParent<CapsuleCollider3DComponent>());
    }

    [Fact]
    public void Tag_SurvivesSerializationRoundTrip()
    {
        var scene = new Scene();
        var e = scene.Instantiate("Tagged");
        e.Tag = "Pickup";

        string json = new SceneSerializer(scene).SerializeToString();

        var loaded = new Scene();
        Assert.True(new SceneSerializer(loaded).DeserializeFromString(json));

        Entity? restored = loaded.FindByTag("Pickup");
        Assert.NotNull(restored);
        Assert.Equal("Tagged", restored!.Value.Name);
    }

    [Fact]
    public void NonGenericComponentAccess_ReturnsNullWhereGenericThrows()
    {
        var scene = new Scene();
        var e = scene.Instantiate();
        e.AddComponent(new BoxCollider3DComponent());

        // Type-based access returns the component when present and null when absent, never throwing.
        Assert.NotNull(e.GetComponent(typeof(BoxCollider3DComponent)));
        Assert.Null(e.GetComponent(typeof(CapsuleCollider3DComponent)));
        Assert.True(e.TryGetComponent(typeof(BoxCollider3DComponent), out object? found));
        Assert.IsType<BoxCollider3DComponent>(found);
        Assert.False(e.TryGetComponent(typeof(CapsuleCollider3DComponent), out object? missing));
        Assert.Null(missing);

        // The generic getter, by contrast, throws when the component is absent.
        Assert.Throws<InvalidOperationException>(() => e.GetComponent<CapsuleCollider3DComponent>());
    }
}
