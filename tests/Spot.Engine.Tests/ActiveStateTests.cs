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
    public void Component_DefaultsToEnabled()
    {
        var scene = new Scene();
        var entity = scene.Instantiate("Entity");
        var sprite = entity.AddComponent(new Sprite2D());

        Assert.True(sprite.Enabled);
    }
}
