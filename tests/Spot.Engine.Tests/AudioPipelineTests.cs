using System.IO;
using Spot.Assets;
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
    public void AudioDecoder_Decodes16BitPcmWav()
    {
        using var temp = new TempDir();
        short[] pcm = { 10, -10, 20, -20, 30, -30 };
        string path = Path.Combine(temp.Path, "sound.wav");
        File.WriteAllBytes(path, MakeWav(ToBytes(pcm), bitsPerSample: 16, channels: 2, sampleRate: 44100));

        short[] decoded = AudioDecoder.Decode(path, out int channels, out int sampleRate);

        Assert.Equal(2, channels);
        Assert.Equal(44100, sampleRate);
        Assert.Equal(pcm, decoded);
    }

    [Fact]
    public void AudioDecoder_Decodes8BitPcmWav_ToSigned16()
    {
        using var temp = new TempDir();
        byte[] samples = { 128, 255, 0 }; // unsigned, centered at 128
        string path = Path.Combine(temp.Path, "sound8.wav");
        File.WriteAllBytes(path, MakeWav(samples, bitsPerSample: 8, channels: 1, sampleRate: 8000));

        short[] decoded = AudioDecoder.Decode(path, out int channels, out int sampleRate);

        Assert.Equal(1, channels);
        Assert.Equal(8000, sampleRate);
        Assert.Equal(new short[] { 0, 127 << 8, -128 << 8 }, decoded);
    }

    [Fact]
    public void AudioImporter_Cooks_WavToSpAudio()
    {
        using var temp = new TempDir();
        short[] pcm = { 5, -5, 15, -15 };
        string path = Path.Combine(temp.Path, "clip.wav");
        File.WriteAllBytes(path, MakeWav(ToBytes(pcm), bitsPerSample: 16, channels: 1, sampleRate: 22050));

        var importer = new AudioImporter();
        AssetMeta meta = AssetMeta.ReadOrCreate(path, importer.Id);
        CookedArtifact artifact = importer.Cook(path, meta, new PassthroughResolver());

        Assert.Equal("audio", artifact.Type);
        SpAudioData cooked = SpAudio.Read(artifact.Bytes);
        Assert.Equal(1, cooked.Channels);
        Assert.Equal(22050, cooked.SampleRate);
        Assert.Equal(pcm, cooked.Pcm);
    }

    private static byte[] ToBytes(short[] pcm)
    {
        byte[] bytes = new byte[pcm.Length * 2];
        for (int i = 0; i < pcm.Length; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), pcm[i]);
        }

        return bytes;
    }

    // Builds a minimal canonical WAVE file (RIFF/fmt /data) around raw sample bytes.
    private static byte[] MakeWav(byte[] data, ushort bitsPerSample, ushort channels, uint sampleRate)
    {
        ushort blockAlign = (ushort)(channels * (bitsPerSample / 8));
        uint byteRate = sampleRate * blockAlign;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36u + (uint)data.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16u);                 // PCM fmt chunk size
        w.Write((ushort)1);           // WAVE_FORMAT_PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);
        w.Write("data"u8);
        w.Write((uint)data.Length);
        w.Write(data);
        w.Flush();
        return ms.ToArray();
    }

    private sealed class PassthroughResolver : IGuidResolver
    {
        public string? ToGuidRef(string sourcePathOrRef) => sourcePathOrRef;
    }
}
