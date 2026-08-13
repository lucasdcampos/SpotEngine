namespace Spot.Assets;

/// <summary>
/// Cooks audio files (<c>.wav</c>, <c>.ogg</c>) into <c>.sptaudio</c>: interleaved 16-bit PCM decoded once here so
/// the runtime uploads it straight to an OpenAL buffer with no audio decoder present. Cooking the compressed
/// OGG to PCM up front trades a larger artifact for a decoder-free, allocation-cheap load at runtime.
/// </summary>
public sealed class AudioImporter : IAssetImporter
{
    /// <inheritdoc />
    public string Id => "audio";

    /// <inheritdoc />
    public IEnumerable<string> SourceExtensions => new[] { ".wav", ".ogg" };

    /// <inheritdoc />
    public string CookedExtension => ".sptaudio";

    /// <inheritdoc />
    public CookedArtifact Cook(string sourcePath, AssetMeta meta, IGuidResolver resolver)
    {
        short[] pcm = AudioDecoder.Decode(sourcePath, out int channels, out int sampleRate);
        byte[] bytes = SpAudio.Write(channels, sampleRate, pcm);
        return new CookedArtifact(bytes, Id);
    }
}
