using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Spot.Animation;
using Spot.Assets;
using Spot.Rendering;
using Xunit;

namespace Spot.Engine.Tests;

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
        Assert.Equal(expected, Spot.Scenes.AnimationSystem.NormalizeBoneName(input));
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
        Assert.Equal(0, skinned.Vertices.Length % Spot.Rendering.Mesh.SkinnedFloatsPerVertex);
        Assert.Contains(skinned.Bones!, b => b.Name.Contains("mixamorig", StringComparison.OrdinalIgnoreCase));
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
