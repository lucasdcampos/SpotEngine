using System.Numerics;
using Spot.Engine.Animation;
using Spot.Engine.Graphics;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the framework's code-only 3D: the camera, the skeleton (pose sampling and skinning palettes) and the
/// basic renderer against a recording device.
/// </summary>
public class Basic3DTests
{
    // One triangle in the rigid layout: position (3), normal (3), uv (2).
    private static readonly float[] RigidTriangle =
    {
        0, 0, 0, 0, 0, 1, 0, 0,
        1, 0, 0, 0, 0, 1, 1, 0,
        0, 1, 0, 0, 0, 1, 0, 1,
    };

    // The same triangle in the skinned layout: + bone indices (4) and weights (4).
    private static readonly float[] SkinnedTriangle =
    {
        0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
        1, 0, 0, 0, 0, 1, 1, 0, 1, 0, 0, 0, 1, 0, 0, 0,
        0, 1, 0, 0, 0, 1, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0,
    };

    private static RecordingGraphicsDevice Install()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        return device;
    }

    private static Mesh Triangle() => new(RigidTriangle, new uint[] { 0, 1, 2 });

    // ---- Camera3D ----

    [Fact]
    public void Camera_ComposesViewAndProjection()
    {
        var camera = new Camera3D { Position = new Vector3(0, 2, 10), Target = Vector3.Zero, FieldOfView = 45f };
        camera.SetViewport(800, 400);

        Assert.Equal(2.0f, camera.AspectRatio);
        Assert.Equal(camera.View * camera.Projection, camera.ViewProjection);
        Assert.Equal(Vector3.Normalize(new Vector3(0, -2, -10)), camera.Forward);

        // The target projects to the center of the screen.
        Vector4 clip = Vector4.Transform(new Vector4(Vector3.Zero, 1), camera.ViewProjection);
        Assert.Equal(0.0f, clip.X / clip.W, 4);
        Assert.Equal(0.0f, clip.Y / clip.W, 4);
    }

    [Fact]
    public void Camera_GuardsDegenerateSettings()
    {
        var camera = new Camera3D { Position = Vector3.Zero, Target = Vector3.Zero, FieldOfView = 500f, NearPlane = -1f };
        camera.SetViewport(0, 100);
        camera.AspectRatio = -2f;

        Assert.Equal(-Vector3.UnitZ, camera.Forward);
        Assert.InRange(camera.FieldOfView, 0.01f, 179f);
        Assert.True(camera.NearPlane > 0f);
        Assert.Equal(16f / 9f, camera.AspectRatio, 4);
        _ = camera.ViewProjection; // must not throw
    }

    [Fact]
    public void Camera_LookAtRetargets()
    {
        var camera = new Camera3D { Position = new Vector3(5, 0, 0) };
        camera.LookAt(new Vector3(5, 0, -3));

        Assert.Equal(-Vector3.UnitZ, camera.Forward);
    }

    // ---- Skeleton ----

    private static ModelNodeInfo Node(string name, Matrix4x4 local, params ModelNodeInfo[] children) =>
        new(name, local, Array.Empty<int>(), children);

    private static Skeleton TwoBoneArm() => Skeleton.FromModelNodes(
        Node("Root", Matrix4x4.Identity,
            Node("mixamorig:Upper", Matrix4x4.CreateTranslation(0, 1, 0),
                Node("mixamorig:Lower", Matrix4x4.CreateTranslation(0, 1, 0)))));

    [Fact]
    public void Skeleton_FlattensParentsBeforeChildren()
    {
        Skeleton skeleton = TwoBoneArm();

        Assert.Equal(new[] { "Root", "mixamorig:Upper", "mixamorig:Lower" }, skeleton.NodeNames);
        Assert.Equal(new[] { -1, 0, 1 }, skeleton.Parents);
        Assert.Equal(2, skeleton.IndexOf("mixamorig7:Lower")); // exporter namespaces are normalized
        Assert.Equal(-1, skeleton.IndexOf("Missing"));
    }

    [Fact]
    public void Skeleton_RestPoseComposesBindTransforms()
    {
        Skeleton skeleton = TwoBoneArm();
        var globals = new Matrix4x4[skeleton.NodeCount];

        skeleton.SamplePose(null, 0f, loop: true, globals);

        Assert.Equal(new Vector3(0, 2, 0), globals[2].Translation);
    }

    [Fact]
    public void Skeleton_SamplesAnimatedNodesAndKeepsTheRestOfTheBindPose()
    {
        Skeleton skeleton = TwoBoneArm();
        var raise = new AnimationChannel("mixamorig5:Upper",
            new[] { new Keyframe<Vector3>(0f, new Vector3(0, 1, 0)), new Keyframe<Vector3>(1f, new Vector3(0, 3, 0)) },
            Array.Empty<Keyframe<Quaternion>>(), Array.Empty<Keyframe<Vector3>>());
        var clip = new AnimationClip("Raise", 1f, new[] { raise });
        var globals = new Matrix4x4[skeleton.NodeCount];

        skeleton.SamplePose(clip, 0.5f, loop: false, globals);
        Assert.Equal(new Vector3(0, 2, 0), globals[1].Translation);
        Assert.Equal(new Vector3(0, 3, 0), globals[2].Translation); // the child follows its parent

        skeleton.SamplePose(clip, 1.5f, loop: true, globals); // wraps to 0.5
        Assert.Equal(new Vector3(0, 2, 0), globals[1].Translation);

        skeleton.SamplePose(clip, 5f, loop: false, globals); // holds the last frame
        Assert.Equal(new Vector3(0, 3, 0), globals[1].Translation);
    }

    [Fact]
    public void Skeleton_RejectsTooSmallBuffers()
    {
        Skeleton skeleton = TwoBoneArm();

        Assert.Throws<ArgumentException>(() => skeleton.SamplePose(null, 0f, true, new Matrix4x4[1]));
        Assert.Throws<ArgumentException>(() => skeleton.ComputeSkinningPalette(
            new[] { new BoneInfo("Root", Matrix4x4.Identity) }, new Matrix4x4[3], Matrix4x4.Identity, Span<Matrix4x4>.Empty));
    }

    [Fact]
    public void Skeleton_PaletteIsInverseBindTimesGlobalTimesWorld()
    {
        Skeleton skeleton = TwoBoneArm();
        var globals = new Matrix4x4[skeleton.NodeCount];
        skeleton.SamplePose(null, 0f, true, globals);
        Matrix4x4 inverseBind = Matrix4x4.CreateTranslation(0, -2, 0);
        Matrix4x4 world = Matrix4x4.CreateTranslation(10, 0, 0);
        BoneInfo[] bones = { new("mixamorig:Lower", inverseBind), new("NotInTheSkeleton", Matrix4x4.Identity) };
        var palette = new Matrix4x4[2];

        skeleton.ComputeSkinningPalette(bones, globals, world, palette);

        // At rest, a bone's inverse bind cancels its global transform: only the world transform remains.
        Assert.Equal(world, palette[0]);
        Assert.Equal(world, palette[1]); // unknown bones follow the model
    }

    // ---- BasicRenderer3D ----

    [Fact]
    public void Scene_EnablesDepthAndCullingAndSetsTheLight()
    {
        RecordingGraphicsDevice device = Install();
        var camera = new Camera3D();

        BasicRenderer3D.BeginScene(camera, lightDirection: new Vector3(0, -2, 0), ambient: 2f);

        Assert.True(device.Capabilities[GraphicsCapability.DepthTest]);
        Assert.True(device.Capabilities[GraphicsCapability.CullFace]);
        Assert.Equal(-Vector3.UnitY, BasicRenderer3D.LightDirection);
        Assert.Equal(camera.ViewProjection, BasicRenderer3D.ViewProjection);

        BasicRenderer3D.EndScene();
        Assert.False(device.Capabilities[GraphicsCapability.DepthTest]);
        Assert.False(device.Capabilities[GraphicsCapability.CullFace]);
    }

    [Fact]
    public void DrawMesh_SetsTheMaterialAndTransformAndDrawsItsIndices()
    {
        RecordingGraphicsDevice device = Install();
        using Mesh mesh = Triangle();
        using var texture = new Texture2D(1, 1, new byte[4]);
        Matrix4x4 transform = Matrix4x4.CreateTranslation(1, 2, 3);

        BasicRenderer3D.BeginScene(Matrix4x4.Identity, ambient: 0.4f);
        BasicRenderer3D.DrawMesh(mesh, transform, new BasicMaterial { Color = new Vector4(1, 0, 0, 1), Texture = texture, Lit = false });
        BasicRenderer3D.EndScene();

        RecordingGraphicsDevice.DrawCall draw = Assert.Single(device.Draws);
        Assert.Equal((3u, mesh.VertexArray.Handle.Id), (draw.Count, draw.VertexArray));
        Assert.Equal(transform, device.Uniform("uModel"));
        Assert.Equal(new Vector4(1, 0, 0, 1), device.Uniform("uColor"));
        Assert.Equal(0, device.Uniform("uLit"));
        Assert.Equal(0.4f, device.Uniform("uAmbient"));
        Assert.Equal(texture.Handle.Id, device.BoundTextures[0]);
    }

    [Fact]
    public void DrawModel_DrawsEverySubmesh()
    {
        RecordingGraphicsDevice device = Install();
        using Mesh a = Triangle();
        using Mesh b = Triangle();
        var model = new Model(new[] { a, b });

        BasicRenderer3D.BeginScene(Matrix4x4.Identity);
        BasicRenderer3D.DrawModel(model, Matrix4x4.Identity);
        BasicRenderer3D.EndScene();

        Assert.Equal(new[] { a.VertexArray.Handle.Id, b.VertexArray.Handle.Id }, device.Draws.Select(d => d.VertexArray));
    }

    [Fact]
    public void DrawMeshInstanced_UploadsTransformsAndColorsInBatches()
    {
        RecordingGraphicsDevice device = Install();
        using Mesh mesh = Triangle();
        Matrix4x4[] transforms = Enumerable.Range(0, 600).Select(i => Matrix4x4.CreateTranslation(i, 0, 0)).ToArray();

        BasicRenderer3D.BeginScene(Matrix4x4.Identity);
        BasicRenderer3D.DrawMeshInstanced(mesh, transforms, new BasicMaterial { Color = new Vector4(0, 1, 0, 1) });
        BasicRenderer3D.EndScene();

        Assert.Equal(new uint[] { 512, 88 }, device.Draws.Select(d => d.Instances));
        float[] lastBatch = device.BufferContents<float>(device.BoundBuffers[BufferKind.Vertex]);
        Assert.Equal(88 * 20, lastBatch.Length);
        Assert.Equal(512f, lastBatch[12]); // first instance of the second batch: translation x = 512
        Assert.Equal(1f, lastBatch[17]);   // its color's green
    }

    [Fact]
    public void DrawMeshInstanced_WithNoTransformsDrawsNothing()
    {
        RecordingGraphicsDevice device = Install();
        using Mesh mesh = Triangle();

        BasicRenderer3D.BeginScene(Matrix4x4.Identity);
        BasicRenderer3D.DrawMeshInstanced(mesh, ReadOnlySpan<Matrix4x4>.Empty);

        Assert.Empty(device.Draws);
    }

    [Fact]
    public void DrawSkinnedMesh_UploadsThePaletteCappedAtMaxBones()
    {
        RecordingGraphicsDevice device = Install();
        using var mesh = new Mesh(SkinnedTriangle, new uint[] { 0, 1, 2 }, skinned: true);
        Matrix4x4[] palette = Enumerable.Repeat(Matrix4x4.Identity, BasicRenderer3D.MaxBones + 10).ToArray();

        BasicRenderer3D.BeginScene(Matrix4x4.Identity);
        BasicRenderer3D.DrawSkinnedMesh(mesh, palette);

        Matrix4x4[] uploaded = Assert.IsType<Matrix4x4[]>(device.Uniform("uBones"));
        Assert.Equal(BasicRenderer3D.MaxBones, uploaded.Length);
        Assert.Single(device.Draws);
    }

    [Fact]
    public void CustomShader_ReceivesTheStandardUniforms()
    {
        RecordingGraphicsDevice device = Install();
        using Mesh mesh = Triangle();
        using var custom = new Shader("custom-vs", "custom-fs");

        BasicRenderer3D.BeginScene(Matrix4x4.Identity);
        BasicRenderer3D.DrawMesh(mesh, Matrix4x4.Identity, new BasicMaterial { Shader = custom });

        Assert.Equal(custom.Handle.Id, Assert.Single(device.Draws).Program);
        Assert.NotNull(device.Uniform("uLightDirection"));
    }

    [Fact]
    public void Renderer_RecreatesItsResourcesOnANewDevice()
    {
        Install();
        using Mesh first = Triangle();
        BasicRenderer3D.BeginScene(Matrix4x4.Identity);
        BasicRenderer3D.DrawMesh(first, Matrix4x4.Identity);

        RecordingGraphicsDevice second = Install();
        using Mesh mesh = Triangle();
        BasicRenderer3D.BeginScene(Matrix4x4.Identity);
        BasicRenderer3D.DrawMesh(mesh, Matrix4x4.Identity);

        Assert.Contains(Assert.Single(second.Draws).Program, second.LivePrograms);
    }

    [Fact]
    public void Mesh_KeepsOneInstancedVertexArrayPerInstanceBuffer()
    {
        Install();
        using Mesh mesh = Triangle();
        using var a = new VertexBuffer(20, ShaderDataType.Float4, ShaderDataType.Float4, ShaderDataType.Float4, ShaderDataType.Float4, ShaderDataType.Float4);
        using var b = new VertexBuffer(20, ShaderDataType.Float4, ShaderDataType.Float4, ShaderDataType.Float4, ShaderDataType.Float4, ShaderDataType.Float4);

        VertexArray forA = mesh.GetInstancedVertexArray(a);

        Assert.Same(forA, mesh.GetInstancedVertexArray(a));
        Assert.NotSame(forA, mesh.GetInstancedVertexArray(b));
    }
}
