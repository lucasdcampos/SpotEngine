using System;
using System.Numerics;

namespace Spot.Audio;

/// <summary>A backend-neutral handle to a playback source (one voice channel).</summary>
public readonly record struct AudioSourceHandle(uint Id)
{
    /// <summary>Gets whether this handle refers to a real source (a non-zero id).</summary>
    public bool IsValid => Id != 0;
}

/// <summary>A backend-neutral handle to an uploaded PCM buffer.</summary>
public readonly record struct AudioBufferHandle(uint Id)
{
    /// <summary>Gets whether this handle refers to a real buffer (a non-zero id).</summary>
    public bool IsValid => Id != 0;
}

/// <summary>The playback state of a source, mirrored across backends.</summary>
public enum AudioSourceState
{
    /// <summary>The source has never been played.</summary>
    Initial,

    /// <summary>The source is currently playing.</summary>
    Playing,

    /// <summary>The source is paused mid-playback.</summary>
    Paused,

    /// <summary>The source has stopped (finished or explicitly stopped).</summary>
    Stopped,
}

/// <summary>The sample layout of a PCM buffer uploaded to a backend.</summary>
public enum AudioSampleFormat
{
    /// <summary>Single-channel 16-bit signed PCM.</summary>
    Mono16,

    /// <summary>Two-channel 16-bit signed PCM (interleaved).</summary>
    Stereo16,
}

/// <summary>
/// The audio device seam: a minimal, GL-style set of source/buffer/listener operations that
/// <see cref="AudioManager"/> issues against. The desktop backend maps these onto OpenAL; the browser
/// backend maps them onto Web Audio. Implementations are thin — <see cref="AudioManager"/> owns the source
/// pool and the never-crash guards, so a backend method may assume it is only called when the device is
/// available.
/// </summary>
public interface IAudioBackend
{
    /// <summary>Gets whether a usable audio device is open. Every call below is a no-op otherwise.</summary>
    bool Available { get; }

    /// <summary>Opens the audio device. Safe to call more than once; returns <see cref="Available"/>.</summary>
    bool Open();

    /// <summary>Closes the audio device and releases any device-owned state.</summary>
    void Close();

    /// <summary>Creates a new playback source.</summary>
    AudioSourceHandle GenSource();

    /// <summary>Deletes a source, freeing its backend resources.</summary>
    void DeleteSource(AudioSourceHandle source);

    /// <summary>Starts (or resumes) playback of a source.</summary>
    void PlaySource(AudioSourceHandle source);

    /// <summary>Stops a source, resetting its playback position.</summary>
    void StopSource(AudioSourceHandle source);

    /// <summary>Pauses a source, preserving its playback position.</summary>
    void PauseSource(AudioSourceHandle source);

    /// <summary>Gets the current playback state of a source.</summary>
    AudioSourceState GetSourceState(AudioSourceHandle source);

    /// <summary>Attaches a buffer to a source for playback (pass a default handle to detach).</summary>
    void SetSourceBuffer(AudioSourceHandle source, AudioBufferHandle buffer);

    /// <summary>Gets the buffer currently attached to a source, or a default handle if none.</summary>
    AudioBufferHandle GetSourceBuffer(AudioSourceHandle source);

    /// <summary>Sets a source's gain (linear volume, clamped by the caller).</summary>
    void SetSourceGain(AudioSourceHandle source, float gain);

    /// <summary>Sets a source's pitch (playback-rate multiplier).</summary>
    void SetSourcePitch(AudioSourceHandle source, float pitch);

    /// <summary>Sets whether a source loops when it reaches the end of its buffer.</summary>
    void SetSourceLooping(AudioSourceHandle source, bool looping);

    /// <summary>Sets whether a source's position is relative to the listener (true for non-spatial sounds).</summary>
    void SetSourceRelative(AudioSourceHandle source, bool relative);

    /// <summary>Sets a source's world position (used only when the source is spatial).</summary>
    void SetSourcePosition(AudioSourceHandle source, Vector3 position);

    /// <summary>Sets the distance at which a spatial source plays at full gain.</summary>
    void SetSourceReferenceDistance(AudioSourceHandle source, float distance);

    /// <summary>Sets the distance beyond which a spatial source no longer attenuates.</summary>
    void SetSourceMaxDistance(AudioSourceHandle source, float distance);

    /// <summary>Creates a new PCM buffer.</summary>
    AudioBufferHandle GenBuffer();

    /// <summary>Deletes a buffer, freeing its backend resources.</summary>
    void DeleteBuffer(AudioBufferHandle buffer);

    /// <summary>Uploads interleaved 16-bit PCM into a buffer.</summary>
    void UploadBuffer(AudioBufferHandle buffer, AudioSampleFormat format, ReadOnlySpan<short> pcm, int sampleRate);

    /// <summary>Sets the master listener gain (the global mix level).</summary>
    void SetListenerGain(float gain);

    /// <summary>Sets the listener's world position.</summary>
    void SetListenerPosition(Vector3 position);

    /// <summary>Sets the listener's orientation from forward and up vectors.</summary>
    void SetListenerOrientation(Vector3 forward, Vector3 up);
}
