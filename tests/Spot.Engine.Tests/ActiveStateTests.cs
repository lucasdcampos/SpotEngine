using System.Numerics;
using Spot.Scenes;
using Spot.Physics;
using Spot.Rendering;
using Xunit;

namespace Spot.Engine.Tests;

public class ActiveStateTests
{
    [Fact]
    public void Entity_IsActiveInHierarchy_DefaultsToTrue()
    {
        var scene = new Scene();
        var entity = scene.Instantiate("Entity");
        
        Assert.True(entity.Enabled);
        Assert.True(entity.IsActiveInHierarchy());
    }

    [Fact]
    public void Entity_IsActiveInHierarchy_DisabledParent_DisablesChild()
    {
        var scene = new Scene();
        var parent = scene.Instantiate("Parent");
        var child = scene.Instantiate("Child");
        child.SetParent(parent);

        Assert.True(child.IsActiveInHierarchy());

        parent.Enabled = false;

        Assert.False(parent.IsActiveInHierarchy());
        Assert.False(child.IsActiveInHierarchy());
    }

    [Fact]
    public void Entity_IsActiveInHierarchy_ReenablingParent_ReactivatesChild()
    {
        var scene = new Scene();
        var parent = scene.Instantiate("Parent");
        var child = scene.Instantiate("Child");
        child.SetParent(parent);

        // Query first so the result is memoized, then toggle: the cache must observe the change.
        Assert.True(child.IsActiveInHierarchy());

        parent.Enabled = false;
        Assert.False(child.IsActiveInHierarchy());

        parent.Enabled = true;
        Assert.True(child.IsActiveInHierarchy());
    }

    [Fact]
    public void Entity_IsActiveInHierarchy_Reparent_UpdatesAfterQuery()
    {
        var scene = new Scene();
        var disabledParent = scene.Instantiate("DisabledParent");
        var enabledParent = scene.Instantiate("EnabledParent");
        var child = scene.Instantiate("Child");

        disabledParent.Enabled = false;
        child.SetParent(disabledParent);

        // Memoize the inactive result, then reparent onto an active parent: the cache must recompute.
        Assert.False(child.IsActiveInHierarchy());

        child.SetParent(enabledParent);
        Assert.True(child.IsActiveInHierarchy());
    }

    [Fact]
    public void Component_DefaultsToEnabled()
    {
        var scene = new Scene();
        var entity = scene.Instantiate("Entity");
        var sprite = entity.AddComponent(new Sprite2DComponent());

        Assert.True(sprite.Enabled);
    }
}
