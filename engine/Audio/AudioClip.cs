
namespace Spot.Engine.Audio;

/// <summary>
/// A fully-decoded sound: interleaved 16-bit PCM held in memory, ready to be uploaded to a backend audio
/// buffer the first time it plays. This is the audio counterpart to <c>Texture2D</c>: build one from PCM, or
/// decode a file with <see cref="FromFile"/>.
/// </summary>
public sealed class AudioClip : IDisposable
{
    /// <summary>The cached backend audio buffer handle (0 until first uploaded by <see cref="AudioManager"/>).</summary>
    internal uint BackendBuffer;

    /// <summary>Initializes a clip from decoded PCM.</summary>
    /// <param name="pcm">Interleaved signed 16-bit PCM samples.</param>
    /// <param name="channels">Channel count (1 = mono, 2 = stereo).</param>
    /// <param name="sampleRate">Sample rate in frames per second.</param>
    /// <exception cref="ArgumentOutOfRangeException">The channel count is not 1 or 2, or the sample rate is not positive.</exception>
    public AudioClip(short[] pcm, int channels, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        Pcm = pcm;
        Channels = channels;
        SampleRate = sampleRate;
    }

    /// <summary>Gets the interleaved signed 16-bit PCM samples.</summary>
    public short[] Pcm { get; }

    /// <summary>Gets the channel count (1 = mono, 2 = stereo).</summary>
    public int Channels { get; }

    /// <summary>Gets the sample rate in frames per second.</summary>
    public int SampleRate { get; }

    /// <summary>Gets the clip length in seconds.</summary>
    public float LengthInSeconds =>
        Channels > 0 && SampleRate > 0 ? (float)Pcm.Length / Channels / SampleRate : 0.0f;

    /// <summary>
    /// Decodes a WAV or OGG/Vorbis file, read through <see cref="Spot.Engine.IO.FileSystem"/>, into a new clip.
    /// </summary>
    /// <param name="path">The audio file path.</param>
    /// <returns>The decoded clip.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="NotSupportedException">The file is neither WAV nor OGG/Vorbis.</exception>
    /// <exception cref="InvalidDataException">The file is malformed.</exception>
    public static AudioClip FromFile(string path)
    {
        short[] pcm = AudioDecoder.Decode(path, out int channels, out int sampleRate);
        return new AudioClip(pcm, channels, sampleRate);
    }

    /// <summary>
    /// Decodes an encoded WAV or OGG/Vorbis file held in memory into a new clip.
    /// </summary>
    /// <param name="encoded">The encoded file contents.</param>
    /// <returns>The decoded clip.</returns>
    /// <exception cref="NotSupportedException">The data is neither WAV nor OGG/Vorbis.</exception>
    /// <exception cref="InvalidDataException">The data is malformed.</exception>
    public static AudioClip FromBytes(byte[] encoded)
    {
        short[] pcm = AudioDecoder.Decode(encoded, out int channels, out int sampleRate);
        return new AudioClip(pcm, channels, sampleRate);
    }

    /// <inheritdoc />
    public void Dispose() => AudioManager.ReleaseClipBuffer(this);
}
