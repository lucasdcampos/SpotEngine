using System.IO;
using Spot.Engine.Assets;
using Spot.Tests;
using Spot.Tests.Fakes;
using Xunit;

namespace Spot.Engine.Tests;

public class AudioPipelineTests
{
    [Fact]
    public void SpAudio_RoundTrips_PcmChannelsAndRate()
    {
        short[] pcm = { 0, 1, -1, 32767, -32768, 100, -100, 42 };

        byte[] blob = SpAudio.Write(channels: 2, sampleRate: 48000, pcm);
        SpAudioData loaded = SpAudio.Read(blob);

        Assert.Equal(2, loaded.Channels);
        Assert.Equal(48000, loaded.SampleRate);
        Assert.Equal(pcm, loaded.Pcm);
    }

    [Fact]
    public void SpAudio_Read_RejectsBadMagic()
    {
        Assert.Throws<InvalidDataException>(() => SpAudio.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    [Fact]
    public void SpAudio_Read_RejectsTruncatedSamples()
    {
        byte[] blob = SpAudio.Write(1, 44100, new short[] { 1, 2, 3, 4 });
        Assert.Throws<InvalidDataException>(() => SpAudio.Read(blob.AsSpan(0, blob.Length - 2)));
    }

    [Fact]
    public void AudioImporter_Cooks_WavToSpAudio()
    {
        using var temp = new TempDir();
        short[] pcm = { 5, -5, 15, -15 };
        string path = Path.Combine(temp.Path, "clip.wav");
        File.WriteAllBytes(path, TestMedia.Wav16(pcm, channels: 1, sampleRate: 22050));

        var importer = new AudioImporter();
        AssetMeta meta = AssetMeta.ReadOrCreate(path, importer.Id);
        CookedArtifact artifact = importer.Cook(path, meta, new PassthroughResolver());

        Assert.Equal("audio", artifact.Type);
        SpAudioData cooked = SpAudio.Read(artifact.Bytes);
        Assert.Equal(1, cooked.Channels);
        Assert.Equal(22050, cooked.SampleRate);
        Assert.Equal(pcm, cooked.Pcm);
    }

    private sealed class PassthroughResolver : IGuidResolver
    {
        public string? ToGuidRef(string sourcePathOrRef) => sourcePathOrRef;
    }
}
