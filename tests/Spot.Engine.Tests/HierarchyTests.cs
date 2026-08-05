using Spot.Scenes;

namespace Spot.Engine.Tests;

public class HierarchyTests
{
    [Fact]
    public void SetParent_LinksParentAndChild()
    {
        var scene = new Scene();
        var parent = scene.Instantiate("parent");
        var child = scene.Instantiate("child");

        child.SetParent(parent);

        Assert.True(child.Parent == parent);
        Assert.Contains(child, parent.Children);
    }

    [Fact]
    public void SetParent_Reparent_MovesFromOldParentToNew()
    {
        var scene = new Scene();
        var a = scene.Instantiate("a");
        var b = scene.Instantiate("b");
        var child = scene.Instantiate("child");

        child.SetParent(a);
        child.SetParent(b);

        Assert.True(child.Parent == b);
        Assert.DoesNotContain(child, a.Children);
        Assert.Contains(child, b.Children);
    }

    [Fact]
    public void SetParent_Null_Detaches()
    {
        var scene = new Scene();
        var parent = scene.Instantiate();
        var child = scene.Instantiate();
        child.SetParent(parent);

        child.SetParent(null);

        Assert.True(child.Parent is null);
        Assert.DoesNotContain(child, parent.Children);
    }

    [Fact]
    public void IsDescendantOf_WalksAncestry()
    {
        var scene = new Scene();
        var root = scene.Instantiate();
        var mid = scene.Instantiate();
        var leaf = scene.Instantiate();
        mid.SetParent(root);
        leaf.SetParent(mid);

        Assert.True(leaf.IsDescendantOf(root));
        Assert.True(leaf.IsDescendantOf(mid));
        Assert.False(root.IsDescendantOf(leaf));
    }

    [Fact]
    public void SetParent_CircularHierarchyIsPrevented()
    {
        var scene = new Scene();
        var root = scene.Instantiate();
        var child = scene.Instantiate();
        child.SetParent(root);

        // Making the ancestor a child of its own descendant must be rejected.
        root.SetParent(child);

        Assert.True(root.Parent is null);
        Assert.True(child.Parent == root);
    }

    [Fact]
    public void Name_RoundTripsThroughLabelComponent()
    {
        var scene = new Scene();
        var e = scene.Instantiate("initial");
        Assert.Equal("initial", e.GetComponent<LabelComponent>().Name);

        e.Name = "renamed";
        Assert.Equal("renamed", e.GetComponent<LabelComponent>().Name);
    }
}
