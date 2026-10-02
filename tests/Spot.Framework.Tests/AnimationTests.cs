using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Spot.Framework.Animation;
using Spot.Framework.Assimp;
using Spot.Framework.Graphics;
using Xunit;

namespace Spot.Framework.Tests;

public class AnimationTests
{
    private static readonly Keyframe<Vector3>[] NoVec = Array.Empty<Keyframe<Vector3>>();
    private static readonly Keyframe<Quaternion>[] NoQuat = Array.Empty<Keyframe<Quaternion>>();

    [Fact]
    public void Channel_SamplesPositionLinearly_AndClampsAtEnds()
    {
        var channel = new AnimationChannel(
            "node",
            new[] { new Keyframe<Vector3>(0f, Vector3.Zero), new Keyframe<Vector3>(2f, new Vector3(10, 0, 0)) },
            NoQuat,
            NoVec);

        Assert.True(channel.TrySamplePosition(1f, out Vector3 mid));
        Assert.Equal(5f, mid.X, 3);

        Assert.True(channel.TrySamplePosition(0.5f, out Vector3 quarter));
        Assert.Equal(2.5f, quarter.X, 3);

        // Before the first / after the last key holds the endpoint value.
        Assert.True(channel.TrySamplePosition(-1f, out Vector3 before));
        Assert.Equal(0f, before.X, 3);
        Assert.True(channel.TrySamplePosition(9f, out Vector3 after));
        Assert.Equal(10f, after.X, 3);
    }

    [Fact]
    public void Channel_WithoutKeys_ReturnsFalseSoTheNodeKeepsItsPose()
    {
        var channel = new AnimationChannel("node", NoVec, NoQuat, NoVec);

        Assert.False(channel.TrySamplePosition(0.5f, out _));
        Assert.False(channel.TrySampleRotation(0.5f, out _));
        Assert.False(channel.TrySampleScale(0.5f, out _));
    }

    [Fact]
    public void Channel_SlerpsRotationHalfway()
    {
        Quaternion a = Quaternion.Identity;
        Quaternion b = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2.0f);
        var channel = new AnimationChannel(
            "node",
            NoVec,
            new[] { new Keyframe<Quaternion>(0f, a), new Keyframe<Quaternion>(1f, b) },
            NoVec);

        Assert.True(channel.TrySampleRotation(0.5f, out Quaternion mid));

        Quaternion expected = Quaternion.Slerp(a, b, 0.5f);
        Assert.True(MathF.Abs(Quaternion.Dot(mid, expected)) > 0.9999f);
    }

    [Theory]
    [InlineData("mixamorig5:Hips", "mixamorig:Hips")]
    [InlineData("mixamorig:Hips", "mixamorig:Hips")]
    [InlineData("mixamorig12:LeftArm_$AssimpFbx$_Rotation", "mixamorig:LeftArm_$AssimpFbx$_Rotation")]
    [InlineData("Alpha_Surface", "Alpha_Surface")]
    public void NormalizeBoneName_CanonicalizesMixamoNamespace(string input, string expected)
    {
        // A clip exported as "mixamorig5:*" must retarget onto a model skinned as "mixamorig:*".
        Assert.Equal(expected, Spot.Framework.Animation.BoneName.Normalize(input));
    }

    [Fact]
    public void Clip_WrapTime_LoopsAndClamps()
    {
        var clip = new AnimationClip("clip", 2.0f, Array.Empty<AnimationChannel>());

        Assert.Equal(0.5f, clip.WrapTime(2.5f, loop: true), 3);   // 2.5 wraps to 0.5
        Assert.Equal(1.5f, clip.WrapTime(-0.5f, loop: true), 3);  // negative wraps into range
        Assert.Equal(2.0f, clip.WrapTime(2.5f, loop: false), 3);  // clamps to the end
        Assert.Equal(0.0f, clip.WrapTime(-0.5f, loop: false), 3); // clamps to the start
    }

    [Fact]
    public void ImportModel_RiggedFbx_ProducesSkinnedSubmeshesWithBoneNames()
    {
        string? path = FindRepoFile(Path.Combine("sandbox", "Assets", "Models", "ybot.fbx"));
        if (path is null)
        {
            // The rigged fixture isn't present in this checkout; nothing to assert.
            return;
        }

        CookedModel model = new AssimpModelImporter().ImportModel(path);

        Assert.NotEmpty(model.Submeshes);
        Assert.Contains(model.Submeshes, s => s.Skinned && s.Bones is { Count: > 0 });

        // Skinned submeshes use the 16-float layout, and a Mixamo rig exposes mixamorig:* bone names.
        MeshData skinned = model.Submeshes.First(s => s.Skinned);
        Assert.Equal(0, skinned.Vertices.Length % Spot.Framework.Graphics.Mesh.SkinnedFloatsPerVertex);
        Assert.Contains(skinned.Bones!, b => b.Name.Contains("mixamorig", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Skeleton_OnARealRig_InverseBindCancelsTheBindPose()
    {
        string? path = FindRepoFile(Path.Combine("sandbox", "Assets", "Models", "ybot.fbx"));
        if (path is null)
        {
            return; // the rigged fixture isn't present in this checkout
        }

        var importer = new AssimpModelImporter();
        CookedModel model = importer.ImportModel(path);
        Skeleton skeleton = Skeleton.FromModelNodes(importer.ImportSceneInfo(path).Root);
        var globals = new Matrix4x4[skeleton.NodeCount];
        skeleton.SamplePose(null, 0f, loop: true, globals);

        // In the rest pose every bone's inverse bind matrix undoes its global transform, so each palette entry is the
        // identity — which only holds if the skeleton composes transforms in the same order and convention the
        // importer bakes inverse binds with (the convention the skinning shaders rely on).
        MeshData skinned = model.Submeshes.First(s => s.Skinned);
        var palette = new Matrix4x4[skinned.Bones!.Count];
        skeleton.ComputeSkinningPalette(skinned.Bones, globals, Matrix4x4.Identity, palette);

        int matched = 0;
        for (int i = 0; i < palette.Length; i++)
        {
            if (skeleton.IndexOf(skinned.Bones[i].Name) < 0)
            {
                continue;
            }

            matched++;
            Vector3 probe = Vector3.Transform(new Vector3(10, 20, 30), palette[i]);
            Assert.True(Vector3.Distance(probe, new Vector3(10, 20, 30)) < 0.5f,
                $"bone {skinned.Bones[i].Name} moved the probe to {probe}");
        }

        Assert.True(matched > 10, "expected the rig's bones to be found in its skeleton");
    }

    private static string? FindRepoFile(string relativePath)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
