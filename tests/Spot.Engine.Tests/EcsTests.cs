using Spot.Engine.Physics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.Engine.Tests;

public class EcsTests
{
    [Fact]
    public void Instantiate_AddsDefaultComponents()
    {
        var scene = new Scene();
        var e = scene.Instantiate("Player");

        Assert.True(e.IsValid);
        Assert.Equal("Player", e.Name);
        Assert.True(e.HasComponent<Label>());
        Assert.True(e.HasComponent<Relationship>());
        Assert.True(e.HasComponent<Transform>());
    }

    [Fact]
    public void AddGetRemoveComponent_RoundTrips()
    {
        var scene = new Scene();
        var e = scene.Instantiate();
        var body = new PhysicsBody2D();

        var added = e.AddComponent(body);
        Assert.Same(body, added);
        Assert.True(e.HasComponent<PhysicsBody2D>());
        Assert.Same(body, e.GetComponent<PhysicsBody2D>());
        Assert.True(e.TryGetComponent(out PhysicsBody2D? fetched));
        Assert.Same(body, fetched);

        e.RemoveComponent<PhysicsBody2D>();
        Assert.False(e.HasComponent<PhysicsBody2D>());
        Assert.False(e.TryGetComponent(out PhysicsBody2D? _));
        Assert.Throws<InvalidOperationException>(() => e.GetComponent<PhysicsBody2D>());
    }

    [Fact]
    public void AddComponent_ReplacesExistingOfSameType()
    {
        var scene = new Scene();
        var e = scene.Instantiate();
        var first = e.AddComponent(new PhysicsBody2D());
        var second = e.AddComponent(new PhysicsBody2D());

        Assert.NotSame(first, second);
        Assert.Same(second, e.GetComponent<PhysicsBody2D>());
    }

    [Fact]
    public void View_ReturnsOnlyEntitiesWithComponent()
    {
        var scene = new Scene();
        var a = scene.Instantiate();
        var b = scene.Instantiate();
        scene.Instantiate(); // no light component
        a.AddComponent(new Light());
        b.AddComponent(new Light());

        Assert.Equal(2, scene.View<Light>().Count);
    }

    [Fact]
    public void ViewTwo_ReturnsIntersection()
    {
        var scene = new Scene();
        var both = scene.Instantiate();
        var onlyBody = scene.Instantiate();
        both.AddComponent(new PhysicsBody2D());
        both.AddComponent(new BoxCollider2D());
        onlyBody.AddComponent(new PhysicsBody2D());

        var view = scene.View<PhysicsBody2D, BoxCollider2D>();

        Assert.Single(view);
        Assert.Equal(both, view[0]);
    }

    [Fact]
    public void View_IsSnapshot_SafeToMutateSceneWhileIterating()
    {
        var scene = new Scene();
        for (int i = 0; i < 3; i++)
        {
            scene.Instantiate().AddComponent(new Light());
        }

        // The view is a materialized snapshot, so adding/destroying during iteration must not throw.
        var exception = Record.Exception(() =>
        {
            foreach (var e in scene.View<Light>())
            {
                scene.Instantiate().AddComponent(new Light());
                scene.Destroy(e);
            }
            scene.FlushDestroyed();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Destroy_IsDeferredUntilFlush()
    {
        var scene = new Scene();
        var e = scene.Instantiate();

        scene.Destroy(e);
        Assert.True(e.IsValid); // still alive until the frame's flush

        scene.FlushDestroyed();
        Assert.False(e.IsValid);
    }

    [Fact]
    public void Destroy_RemovesChildrenRecursively()
    {
        var scene = new Scene();
        var parent = scene.Instantiate("parent");
        var child = scene.Instantiate("child");
        child.SetParent(parent);

        scene.Destroy(parent);
        scene.FlushDestroyed();

        Assert.False(parent.IsValid);
        Assert.False(child.IsValid);
    }
}
