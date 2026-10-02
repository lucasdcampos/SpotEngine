using System.IO;
using Spot.Framework.Audio;
using Spot.Tests.Fakes;
using Xunit;
using Spot.Tests;

namespace Spot.Framework.Tests;

public class AudioDecoderTests
{
    [Fact]
    public void AudioDecoder_Decodes16BitPcmWav()
    {
        using var temp = new TempDir();
        short[] pcm = { 10, -10, 20, -20, 30, -30 };
        string path = Path.Combine(temp.Path, "sound.wav");
        File.WriteAllBytes(path, TestMedia.Wav16(pcm, channels: 2, sampleRate: 44100));

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
        File.WriteAllBytes(path, TestMedia.Wav(samples, bitsPerSample: 8, channels: 1, sampleRate: 8000));

        short[] decoded = AudioDecoder.Decode(path, out int channels, out int sampleRate);

        Assert.Equal(1, channels);
        Assert.Equal(8000, sampleRate);
        Assert.Equal(new short[] { 0, 127 << 8, -128 << 8 }, decoded);
    }
}
