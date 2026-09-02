using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Spot.Audio;

namespace Spot.Browser;

/// <summary>
/// The browser <see cref="IAudioBackend"/>: maps the neutral source/buffer/listener operations onto the
/// Web Audio API over <c>[JSImport]</c>, the audio counterpart to <see cref="Rendering.WebGL2GraphicsDevice"/>.
/// Every call forwards to a thin JavaScript surface (registered by the browser host under the <c>spot-audio</c>
/// module) that owns a single <c>AudioContext</c> and keeps integer handle tables for buffers and voices — so
/// C# refers to sources and buffers by <c>uint</c>, exactly as the desktop backend refers to OpenAL names.
/// </summary>
/// <remarks>
/// The mismatches between OpenAL's persistent sources and Web Audio's one-shot <c>AudioBufferSourceNode</c>
/// (recreating a node per play, tracking playback state, pause/resume via a saved offset, and spatial routing
/// through a <c>PannerNode</c>) are handled entirely on the JavaScript side; this type stays as thin as the
/// desktop backend. <see cref="AudioManager"/> owns pooling and the never-crash guards, so a method here may
/// assume it is only called when the device is available.
///
/// <para>The JavaScript host must register the <c>spot-audio</c> module (via <c>setModuleImports</c>) with an
/// <c>audio</c> object implementing each imported function below over a live <c>AudioContext</c>.</para>
/// </remarks>
internal sealed partial class WebAudioBackend : IAudioBackend
{
    private const string Module = "spot-audio";

    private bool _available;

    /// <inheritdoc />
    public bool Available => _available;

    /// <inheritdoc />
    public bool Open()
    {
        _available = JsOpen();
        return _available;
    }

    /// <inheritdoc />
    public void Close()
    {
        JsClose();
        _available = false;
    }

    /// <inheritdoc />
    public AudioSourceHandle GenSource() => new((uint)JsGenSource());

    /// <inheritdoc />
    public void DeleteSource(AudioSourceHandle source) => JsDeleteSource((int)source.Id);

    /// <inheritdoc />
    public void PlaySource(AudioSourceHandle source) => JsPlaySource((int)source.Id);

    /// <inheritdoc />
    public void StopSource(AudioSourceHandle source) => JsStopSource((int)source.Id);

    /// <inheritdoc />
    public void PauseSource(AudioSourceHandle source) => JsPauseSource((int)source.Id);

    /// <inheritdoc />
    public AudioSourceState GetSourceState(AudioSourceHandle source) => JsGetSourceState((int)source.Id) switch
    {
        1 => AudioSourceState.Playing,
        2 => AudioSourceState.Paused,
        3 => AudioSourceState.Stopped,
        _ => AudioSourceState.Initial,
    };

    /// <inheritdoc />
    public void SetSourceBuffer(AudioSourceHandle source, AudioBufferHandle buffer) =>
        JsSetSourceBuffer((int)source.Id, (int)buffer.Id);

    /// <inheritdoc />
    public AudioBufferHandle GetSourceBuffer(AudioSourceHandle source) =>
        new((uint)JsGetSourceBuffer((int)source.Id));

    /// <inheritdoc />
    public void SetSourceGain(AudioSourceHandle source, float gain) => JsSetSourceGain((int)source.Id, gain);

    /// <inheritdoc />
    public void SetSourcePitch(AudioSourceHandle source, float pitch) => JsSetSourcePitch((int)source.Id, pitch);

    /// <inheritdoc />
    public void SetSourceLooping(AudioSourceHandle source, bool looping) =>
        JsSetSourceLooping((int)source.Id, looping);

    /// <inheritdoc />
    public void SetSourceRelative(AudioSourceHandle source, bool relative) =>
        JsSetSourceRelative((int)source.Id, relative);

    /// <inheritdoc />
    public void SetSourcePosition(AudioSourceHandle source, Vector3 position) =>
        JsSetSourcePosition((int)source.Id, position.X, position.Y, position.Z);

    /// <inheritdoc />
    public void SetSourceReferenceDistance(AudioSourceHandle source, float distance) =>
        JsSetSourceReferenceDistance((int)source.Id, distance);

    /// <inheritdoc />
    public void SetSourceMaxDistance(AudioSourceHandle source, float distance) =>
        JsSetSourceMaxDistance((int)source.Id, distance);

    /// <inheritdoc />
    public AudioBufferHandle GenBuffer() => new((uint)JsGenBuffer());

    /// <inheritdoc />
    public void DeleteBuffer(AudioBufferHandle buffer) => JsDeleteBuffer((int)buffer.Id);

    /// <inheritdoc />
    public void UploadBuffer(AudioBufferHandle buffer, AudioSampleFormat format, ReadOnlySpan<short> pcm, int sampleRate)
    {
        int channels = format == AudioSampleFormat.Stereo16 ? 2 : 1;
        JsUploadBuffer((int)buffer.Id, channels, sampleRate, AsWritableBytes(pcm));
    }

    /// <inheritdoc />
    public void SetListenerGain(float gain) => JsSetListenerGain(gain);

    /// <inheritdoc />
    public void SetListenerPosition(Vector3 position) => JsSetListenerPosition(position.X, position.Y, position.Z);

    /// <inheritdoc />
    public void SetListenerOrientation(Vector3 forward, Vector3 up) =>
        JsSetListenerOrientation(forward.X, forward.Y, forward.Z, up.X, up.Y, up.Z);

    // Reinterprets a read-only span as a writable byte span for MemoryView marshaling. The JS side only reads
    // the view within the synchronous call (it copies the PCM into an AudioBuffer immediately), so exposing it
    // as writable is safe. Mirrors WebGL2GraphicsDevice.AsWritableBytes.
    private static Span<byte> AsWritableBytes<T>(ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);
        return MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in MemoryMarshal.GetReference(bytes)), bytes.Length);
    }

    // ---- The Web Audio interop surface, implemented by the host's `spot-audio` JS module over one AudioContext. ----

    [JSImport("audio.open", Module)]
    private static partial bool JsOpen();

    [JSImport("audio.close", Module)]
    private static partial void JsClose();

    [JSImport("audio.genSource", Module)]
    private static partial int JsGenSource();

    [JSImport("audio.deleteSource", Module)]
    private static partial void JsDeleteSource(int source);

    [JSImport("audio.playSource", Module)]
    private static partial void JsPlaySource(int source);

    [JSImport("audio.stopSource", Module)]
    private static partial void JsStopSource(int source);

    [JSImport("audio.pauseSource", Module)]
    private static partial void JsPauseSource(int source);

    [JSImport("audio.getSourceState", Module)]
    private static partial int JsGetSourceState(int source);

    [JSImport("audio.setSourceBuffer", Module)]
    private static partial void JsSetSourceBuffer(int source, int buffer);

    [JSImport("audio.getSourceBuffer", Module)]
    private static partial int JsGetSourceBuffer(int source);

    [JSImport("audio.setSourceGain", Module)]
    private static partial void JsSetSourceGain(int source, double gain);

    [JSImport("audio.setSourcePitch", Module)]
    private static partial void JsSetSourcePitch(int source, double pitch);

    [JSImport("audio.setSourceLooping", Module)]
    private static partial void JsSetSourceLooping(int source, bool looping);

    [JSImport("audio.setSourceRelative", Module)]
    private static partial void JsSetSourceRelative(int source, bool relative);

    [JSImport("audio.setSourcePosition", Module)]
    private static partial void JsSetSourcePosition(int source, double x, double y, double z);

    [JSImport("audio.setSourceReferenceDistance", Module)]
    private static partial void JsSetSourceReferenceDistance(int source, double distance);

    [JSImport("audio.setSourceMaxDistance", Module)]
    private static partial void JsSetSourceMaxDistance(int source, double distance);

    [JSImport("audio.genBuffer", Module)]
    private static partial int JsGenBuffer();

    [JSImport("audio.deleteBuffer", Module)]
    private static partial void JsDeleteBuffer(int buffer);

    [JSImport("audio.uploadBuffer", Module)]
    private static partial void JsUploadBuffer(
        int buffer, int channels, int sampleRate, [JSMarshalAs<JSType.MemoryView>] Span<byte> pcm);

    [JSImport("audio.setListenerGain", Module)]
    private static partial void JsSetListenerGain(double gain);

    [JSImport("audio.setListenerPosition", Module)]
    private static partial void JsSetListenerPosition(double x, double y, double z);

    [JSImport("audio.setListenerOrientation", Module)]
    private static partial void JsSetListenerOrientation(double fx, double fy, double fz, double ux, double uy, double uz);
}
