using System.IO;
using Spot.Engine.Assets;
using Spot.Framework.Audio;
using Spot.Framework.Graphics;
using Spot.Framework.IO;
using Spot.Tests;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the engine's asset-reference loading (project paths and guid: references to cooked artifacts)
/// layered on the framework loaders.
/// </summary>
public class AssetLoadingTests
{
    // A 2x2 image, rows top-to-bottom: red, green / blue, white.
    private static readonly byte[] TopDownPixels =
    {
        255, 0, 0, 255, 0, 255, 0, 255,
        0, 0, 255, 255, 255, 255, 255, 255,
    };

    private static RecordingGraphicsDevice InstallDevice()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        return device;
    }

    [Fact]
    public void AssetPathRoot_InstallsAProjectRelativeResolver()
    {
        string previous = AssetPath.Root;
        using var dir = new TempDir();
        try
        {
            AssetPath.Root = dir.Path;

            Assert.Equal(Path.Combine(dir.Path, "Textures/a.png"), FileSystem.Resolve("Textures/a.png"));
            Assert.Equal(Path.Combine(dir.Path, "x"), FileSystem.Resolve(Path.Combine(dir.Path, "x")));
        }
        finally
        {
            AssetPath.Root = previous;
            FileSystem.PathResolver = null;
        }
    }

    [Fact]
    public void AssetLoading_SourcePathsResolveAgainstTheAssetRoot()
    {
        string previous = AssetPath.Root;
        using var dir = new TempDir();
        short[] pcm = { 3, -3 };
        File.WriteAllBytes(Path.Combine(dir.Path, "beep.wav"), TestMedia.Wav16(pcm, 1, 8000));
        try
        {
            AssetPath.Root = dir.Path;

            Assert.Equal(pcm, AudioClip.Load("beep.wav").Pcm);
        }
        finally
        {
            AssetPath.Root = previous;
            FileSystem.PathResolver = null;
        }
    }

    [Fact]
    public void AssetLoading_GuidReferencesLoadTheCookedArtifact()
    {
        using var dir = new TempDir();
        string cooked = Path.Combine(dir.Path, "clip.sptaudio");
        File.WriteAllBytes(cooked, SpAudio.Write(channels: 1, sampleRate: 16000, new short[] { 9, -9 }));
        Func<string, string?>? previous = AssetPath.ContentResolver;
        try
        {
            AssetPath.ContentResolver = reference => reference == "guid:abc" ? cooked : null;

            AudioClip clip = AudioClip.Load("guid:abc");

            Assert.Equal(new short[] { 9, -9 }, clip.Pcm);
            Assert.Equal(16000, clip.SampleRate);
        }
        finally
        {
            AssetPath.ContentResolver = previous;
        }
    }

    [Fact]
    public void AssetLoading_UnresolvedGuidThrowsFileNotFound()
    {
        Func<string, string?>? previous = AssetPath.ContentResolver;
        try
        {
            AssetPath.ContentResolver = _ => null;

            Assert.Throws<FileNotFoundException>(() => AudioClip.Load("guid:missing"));
            Assert.Throws<FileNotFoundException>(() => Texture2D.Load("guid:missing"));
            Assert.Throws<FileNotFoundException>(() => Font.Load("guid:missing"));
        }
        finally
        {
            AssetPath.ContentResolver = previous;
        }
    }

    [Fact]
    public void AssetLoading_CookedTextureUploadsVerbatim()
    {
        RecordingGraphicsDevice device = InstallDevice();
        using var dir = new TempDir();
        string cooked = Path.Combine(dir.Path, "t.spttex");
        File.WriteAllBytes(cooked, SpTex.Write(2, 2, TopDownPixels, pointFilter: true));

        using Texture2D texture = Texture2D.FromSpTex(cooked);

        Assert.Equal(TopDownPixels, device.TextureImages[texture.Handle.Id].Data);
        Assert.Equal((TextureFilter.Nearest, TextureFilter.Nearest), device.TextureFilters[texture.Handle.Id]);
    }
}
