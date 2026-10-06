using System.Numerics;
using Spot.Engine.Assets;
using Spot.Engine.Physics;
using Spot.Engine;
using Spot.Engine.Scenes;
using Spot.Engine.Graphics;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers fitting 3D colliders to the mesh an entity draws (exactly for built-in shapes, from model bounds
/// otherwise) and re-pointing a mesh renderer at another model reference.
/// </summary>
public class ColliderFittingTests
{
    public ColliderFittingTests()
    {
        Renderer.Init(new RecordingGraphicsDevice());
        EngineAssets.Install();
    }

    private static MeshRenderer Builtin(string spec) => new() { ModelPath = "builtin:Mesh/" + spec };

    [Fact]
    public void Box_MatchesABuiltinCube()
    {
        var box = new BoxCollider3D { Offset = Vector3.One };

        Assert.True(ColliderFitting.FitToMesh(box, Builtin("Cube?width=2&height=0.5&depth=3")));

        Assert.Equal(new Vector3(2, 0.5f, 3), box.Size);
        Assert.Equal(Vector3.Zero, box.Offset);
    }

    [Fact]
    public void Capsule_MatchesABuiltinCapsuleExactly()
    {
        var capsule = new CapsuleCollider3D();

        Assert.True(ColliderFitting.FitToMesh(capsule, Builtin("Capsule?radius=0.3&height=1.7")));

        Assert.Equal(0.3f, capsule.Radius, 4);
        Assert.Equal(1.1f, capsule.Length, 4); // the straight section: height minus both caps
    }

    [Fact]
    public void Sphere_WrapsTheMesh()
    {
        var sphere = new SphereCollider3D();

        Assert.True(ColliderFitting.FitToMesh(sphere, Builtin("Sphere?radius=2")));
        Assert.Equal(2f, sphere.Radius, 4);

        Assert.True(ColliderFitting.FitToMesh(sphere, Builtin("Cube?width=1&height=4&depth=1")));
        Assert.Equal(2f, sphere.Radius, 4);
    }

    [Fact]
    public void FlatShapes_KeepASliverOfThickness()
    {
        var box = new BoxCollider3D();

        Assert.True(ColliderFitting.FitToMesh(box, Builtin("Plane?width=4&depth=6")));

        Assert.Equal(new Vector3(4, 0.01f, 6), box.Size);
    }

    [Fact]
    public void Models_FitTheirLoadedBounds_OrTheSubmeshTheyDraw()
    {
        MeshData near = PrimitiveSpec.Parse("Cube").Build();
        MeshData far = Shift(PrimitiveSpec.Parse("Cube?width=2").Build(), new Vector3(5, 0, 0));
        var model = new Model(new[] { new Mesh(near.Vertices, near.Indices), new Mesh(far.Vertices, far.Indices) });
        var whole = new MeshRenderer { ModelPath = "Models/ship.fbx", Model = model };
        var part = new MeshRenderer { ModelPath = "Models/ship.fbx", Model = model, SubmeshIndex = 1 };
        var box = new BoxCollider3D();

        Assert.True(ColliderFitting.FitToMesh(box, whole));
        Assert.Equal(new Vector3(6.5f, 1, 1), box.Size);
        Assert.Equal(new Vector3(2.75f, 0, 0), box.Offset);

        Assert.True(ColliderFitting.FitToMesh(box, part));
        Assert.Equal(new Vector3(2, 1, 1), box.Size);
        Assert.Equal(new Vector3(5, 0, 0), box.Offset);
    }

    [Fact]
    public void UnknownGeometry_LeavesTheColliderAlone()
    {
        var box = new BoxCollider3D { Size = new Vector3(7) };

        Assert.False(ColliderFitting.FitToMesh(box, new MeshRenderer { ModelPath = "Models/still-loading.fbx" }));
        Assert.Null(ColliderFitting.MeshBounds(new MeshRenderer()));
        Assert.Equal(new Vector3(7), box.Size);
    }

    [Fact]
    public void SetModel_LoadsBuiltinsAtOnceAndLeavesFilesToTheRenderer()
    {
        var mesh = new MeshRenderer { Model = PrimitiveModelFactory.Get("Cube") };

        mesh.SetModel("primitive:Cone?segments=6");
        Assert.Equal("builtin:Mesh/Cone?segments=6", mesh.ModelPath);
        Assert.Same(PrimitiveModelFactory.Get("Cone?segments=6"), mesh.Model);

        mesh.SetModel("Models/ship.fbx");
        Assert.Equal("Models/ship.fbx", mesh.ModelPath);
        Assert.Null(mesh.Model);

        mesh.SetModel(null);
        Assert.Null(mesh.ModelPath);
        Assert.Null(mesh.Model);
    }

    private static MeshData Shift(MeshData mesh, Vector3 offset)
    {
        float[] vertices = (float[])mesh.Vertices.Clone();
        for (int i = 0; i < vertices.Length; i += Mesh.FloatsPerVertex)
        {
            vertices[i] += offset.X;
            vertices[i + 1] += offset.Y;
            vertices[i + 2] += offset.Z;
        }

        return new MeshData(vertices, mesh.Indices);
    }
}
