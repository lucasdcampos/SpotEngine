using Spot.Assets;

namespace Spot.Audio;

/// <summary>
/// A fully-decoded sound: interleaved 16-bit PCM held in memory, ready to be uploaded to an OpenAL buffer
/// the first time it plays. This is the audio counterpart to <c>Texture2D</c> — the runtime asset that
/// components reference. Loading follows the same rule as every other asset: a <c>guid:</c> reference
/// resolves to its cooked <c>.spaudio</c>, while any other value is decoded from a source <c>.wav</c>/<c>.ogg</c>.
/// </summary>
public sealed class AudioClip : IDisposable
{
    /// <summary>The cached OpenAL buffer handle (0 until first uploaded by <see cref="AudioManager"/>).</summary>
    internal uint AlBuffer;

    /// <summary>Initializes a clip from decoded PCM.</summary>
    /// <param name="pcm">Interleaved signed 16-bit PCM samples.</param>
    /// <param name="channels">Channel count (1 = mono, 2 = stereo).</param>
    /// <param name="sampleRate">Sample rate in frames per second.</param>
    public AudioClip(short[] pcm, int channels, int sampleRate)
    {
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

    /// <summary>Loads a cooked <c>.spaudio</c> clip — PCM decoded at import time — with no audio decoder at runtime.</summary>
    /// <param name="path">The absolute path to the cooked <c>.spaudio</c> file.</param>
    public static AudioClip FromSpAudio(string path)
    {
        SpAudioData data = SpAudio.ReadFile(path);
        return new AudioClip(data.Pcm, data.Channels, data.SampleRate);
    }

    /// <summary>
    /// Loads a clip from a stored reference: a <c>guid:</c> reference resolves to its cooked <c>.spaudio</c>
    /// through the content host, while any other value is decoded from a source audio path. This is the single
    /// entry point components use, so they never care whether the project has been cooked.
    /// </summary>
    /// <param name="storedRef">The stored reference (a <c>guid:</c> reference or a source audio path).</param>
    /// <exception cref="FileNotFoundException">A <c>guid:</c> reference has no cooked artifact.</exception>
    public static AudioClip Load(string storedRef)
    {
        if (AssetRef.IsGuidRef(storedRef))
        {
            string cooked = AssetPath.ResolveContent(storedRef)
                ?? throw new FileNotFoundException($"Unresolved audio reference '{storedRef}'.");
            return FromSpAudio(cooked);
        }

        string resolved = AssetPath.Resolve(storedRef);
        short[] pcm = AudioDecoder.Decode(resolved, out int channels, out int sampleRate);
        return new AudioClip(pcm, channels, sampleRate);
    }

    /// <inheritdoc />
    public void Dispose() => AudioManager.ReleaseClipBuffer(this);
}
