using System.Numerics;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Engine.Tests;

public class TransformTests
{
    [Fact]
    public void LocalMatrix_EncodesTranslationAndScale()
    {
        var t = new Transform { Position = new Vector3(1, 2, 3), Scale = new Vector3(2, 2, 2) };
        Matrix4x4 m = t.LocalMatrix;

        Assert.Equal(1.0, m.Translation.X, 3);
        Assert.Equal(2.0, m.Translation.Y, 3);
        Assert.Equal(3.0, m.Translation.Z, 3);
        Assert.Equal(2.0, m.M11, 3); // scale along X with no rotation
    }

    [Fact]
    public void Matrix_ComposesWithParentTransform()
    {
        var scene = new Scene();
        var parent = scene.Instantiate();
        var child = scene.Instantiate();
        parent.GetComponent<Transform>().Position = new Vector3(10, 0, 0);
        child.GetComponent<Transform>().Position = new Vector3(5, 0, 0);
        child.SetParent(parent);

        Vector3 world = child.GetComponent<Transform>().WorldPosition;

        Assert.Equal(15.0, world.X, 3);
        Assert.Equal(0.0, world.Y, 3);
    }

    [Fact]
    public void WorldRotation_AccumulatesParentRotation()
    {
        var scene = new Scene();
        var parent = scene.Instantiate();
        var child = scene.Instantiate();
        parent.GetComponent<Transform>().Rotation = new Vector3(0, 0, 90);
        child.GetComponent<Transform>().Rotation = new Vector3(0, 0, 30);
        child.SetParent(parent);

        Assert.Equal(120.0, child.GetComponent<Transform>().WorldRotation.Z, 3);
    }

    [Fact]
    public void Matrix_WithoutParent_EqualsLocalMatrix()
    {
        var scene = new Scene();
        var e = scene.Instantiate();
        var t = e.GetComponent<Transform>();
        t.Position = new Vector3(3, 4, 5);

        Assert.Equal(t.LocalMatrix, t.Matrix);
    }
}
