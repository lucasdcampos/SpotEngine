using System.IO;
using Spot.Assets;
using Spot.Audio;
using Spot.Engine.Tests.Fakes;
using Spot.IO;
using Spot.Rendering;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers loading resources from files: the framework's decoders and file-system indirection (Image, Texture2D,
/// AudioClip, Font) and the engine's asset-reference loaders layered on top of them.
/// </summary>
public class ResourceLoadingTests
{
    // A 2x2 image, rows top-to-bottom as stored in the file: red, green / blue, white.
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
    public void Image_FromBytes_DecodesPngUprightWhenNotFlipped()
    {
        Image image = Image.FromBytes(TestMedia.Png(2, 2, TopDownPixels), flipVertically: false);

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(TopDownPixels, image.Pixels);
    }

    [Fact]
    public void Image_FromBytes_FlipsRowsBottomToTopByDefault()
    {
        Image image = Image.FromBytes(TestMedia.Png(2, 2, TopDownPixels));

        Assert.Equal(TopDownPixels[8..], image.Pixels[..8]);
        Assert.Equal(TopDownPixels[..8], image.Pixels[8..]);
    }

    [Fact]
    public void Image_FromBytes_RejectsGarbageAsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => Image.FromBytes(new byte[] { 1, 2, 3, 4, 5 }));
    }

    [Fact]
    public void Image_FromStream_MatchesFromBytes()
    {
        byte[] png = TestMedia.Png(2, 2, TopDownPixels);
        using var stream = new MemoryStream(png);

        Assert.Equal(Image.FromBytes(png).Pixels, Image.FromStream(stream).Pixels);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Image_FlipVerticallyTwiceIsIdentity(int height)
    {
        byte[] pixels = Enumerable.Range(0, height * 4).Select(i => (byte)i).ToArray();
        var image = new Image(1, height, (byte[])pixels.Clone());

        image.FlipVertically();
        if (height > 1)
        {
            Assert.NotEqual(pixels, image.Pixels);
        }

        image.FlipVertically();
        Assert.Equal(pixels, image.Pixels);
    }

    [Fact]
    public void Image_RejectsMismatchedPixelData()
    {
        Assert.Throws<ArgumentException>(() => new Image(2, 2, new byte[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Image(0, 2, Array.Empty<byte>()));
    }

    [Fact]
    public void Image_ToTexture_UploadsItsPixels()
    {
        RecordingGraphicsDevice device = InstallDevice();
        var image = new Image(2, 2, TopDownPixels);

        using Texture2D texture = image.ToTexture(pointFilter: true);

        Assert.Equal(TopDownPixels, device.TextureImages[texture.Handle.Id].Data);
        Assert.Equal((TextureFilter.Nearest, TextureFilter.Nearest), device.TextureFilters[texture.Handle.Id]);
    }

    [Fact]
    public void Texture2D_RejectsPixelDataOfTheWrongSize()
    {
        InstallDevice();

        Assert.Throws<ArgumentException>(() => new Texture2D(2, 2, new byte[12]));
    }

    [Fact]
    public void Texture2D_FromFile_ReadsThroughTheFileSystem()
    {
        RecordingGraphicsDevice device = InstallDevice();
        using InMemoryFileSystem fs = InMemoryFileSystem.Install().Add("art/logo.png", TestMedia.Png(2, 2, TopDownPixels));

        using Texture2D texture = Texture2D.FromFile("art/logo.png");

        Assert.Equal(2u, texture.Width);
        Assert.Equal(new[] { "art/logo.png" }, fs.Reads);
        Assert.Equal(TopDownPixels[8..], device.TextureImages[texture.Handle.Id].Data[..8]);
    }

    [Fact]
    public void FileSystem_AppliesThePathResolver()
    {
        using InMemoryFileSystem fs = InMemoryFileSystem.Install().Add("root/a.bin", new byte[] { 7 });
        FileSystem.PathResolver = path => "root/" + path;

        Assert.Equal("root/a.bin", FileSystem.Resolve("a.bin"));
        Assert.True(FileSystem.Exists("a.bin"));
        Assert.Equal(new byte[] { 7 }, FileSystem.ReadAllBytes("a.bin"));
        Assert.Equal("\u0007", FileSystem.ReadAllText("a.bin"));
    }

    [Fact]
    public void FileSystem_WithoutResolver_UsesPathsUnchanged()
    {
        FileSystem.PathResolver = null;

        Assert.Equal("some/path.txt", FileSystem.Resolve("some/path.txt"));
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
    public void AudioDecoder_DetectsTheFormatFromContentNotExtension()
    {
        short[] pcm = { 1, -1, 2, -2 };
        using InMemoryFileSystem fs = InMemoryFileSystem.Install().Add("sound.ogg", TestMedia.Wav16(pcm, 1, 11025));

        short[] decoded = AudioDecoder.Decode("sound.ogg", out int channels, out int rate);

        Assert.Equal(pcm, decoded);
        Assert.Equal((1, 11025), (channels, rate));
    }

    [Fact]
    public void AudioDecoder_RejectsUnknownFormats()
    {
        Assert.Throws<NotSupportedException>(() => AudioDecoder.Decode(new byte[] { 0, 1, 2, 3, 4 }, out _, out _));
        Assert.Throws<NotSupportedException>(() => AudioDecoder.Decode(Array.Empty<byte>(), out _, out _));
    }

    [Fact]
    public void AudioClip_FromBytesAndFromFile_DecodePcm()
    {
        short[] pcm = { 100, -100, 200, -200 };
        byte[] wav = TestMedia.Wav16(pcm, 2, 22050);
        using InMemoryFileSystem fs = InMemoryFileSystem.Install().Add("sfx/hit.wav", wav);

        AudioClip fromBytes = AudioClip.FromBytes(wav);
        AudioClip fromFile = AudioClip.FromFile("sfx/hit.wav");

        foreach (AudioClip clip in new[] { fromBytes, fromFile })
        {
            Assert.Equal(pcm, clip.Pcm);
            Assert.Equal(2, clip.Channels);
            Assert.Equal(22050, clip.SampleRate);
            Assert.Equal(2f / 22050f, clip.LengthInSeconds, 6);
        }
    }

    [Theory]
    [InlineData(0, 44100)]
    [InlineData(3, 44100)]
    [InlineData(1, 0)]
    public void AudioClip_RejectsInvalidFormat(int channels, int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioClip(new short[2], channels, sampleRate));
    }

    [Fact]
    public void Font_FromFile_ParsesTheFontAndUploadsItsAtlas()
    {
        RecordingGraphicsDevice device = InstallDevice();
        using InMemoryFileSystem fs = InMemoryFileSystem.Install().Add("fonts/Body.ttf", DefaultFontBytes());

        using Font font = Font.FromFile("fonts/Body.ttf");

        Assert.Equal("Body", font.Name);
        Assert.True(font.TryGetGlyph('A', out _));
        Assert.Contains(font.Atlas.Handle.Id, device.LiveTextures);
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

    private static byte[] DefaultFontBytes()
    {
        using Stream stream = typeof(Font).Assembly.GetManifestResourceStream("Spot.DefaultFont.ttf")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
