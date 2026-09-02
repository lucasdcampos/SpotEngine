using System;
using System.Numerics;

namespace Spot.Audio;

/// <summary>
/// The default <see cref="IAudioBackend"/>: reports the device as unavailable and no-ops every call. It keeps
/// <see cref="AudioManager"/> decoupled from any concrete backend before one is installed, and lets a host
/// that runs without audio (headless tests, a platform with no audio device) degrade cleanly to silence.
/// </summary>
internal sealed class SilentAudioBackend : IAudioBackend
{
    public bool Available => false;

    public bool Open() => false;

    public void Close()
    {
    }

    public AudioSourceHandle GenSource() => default;

    public void DeleteSource(AudioSourceHandle source)
    {
    }

    public void PlaySource(AudioSourceHandle source)
    {
    }

    public void StopSource(AudioSourceHandle source)
    {
    }

    public void PauseSource(AudioSourceHandle source)
    {
    }

    public AudioSourceState GetSourceState(AudioSourceHandle source) => AudioSourceState.Initial;

    public void SetSourceBuffer(AudioSourceHandle source, AudioBufferHandle buffer)
    {
    }

    public AudioBufferHandle GetSourceBuffer(AudioSourceHandle source) => default;

    public void SetSourceGain(AudioSourceHandle source, float gain)
    {
    }

    public void SetSourcePitch(AudioSourceHandle source, float pitch)
    {
    }

    public void SetSourceLooping(AudioSourceHandle source, bool looping)
    {
    }

    public void SetSourceRelative(AudioSourceHandle source, bool relative)
    {
    }

    public void SetSourcePosition(AudioSourceHandle source, Vector3 position)
    {
    }

    public void SetSourceReferenceDistance(AudioSourceHandle source, float distance)
    {
    }

    public void SetSourceMaxDistance(AudioSourceHandle source, float distance)
    {
    }

    public AudioBufferHandle GenBuffer() => default;

    public void DeleteBuffer(AudioBufferHandle buffer)
    {
    }

    public void UploadBuffer(AudioBufferHandle buffer, AudioSampleFormat format, ReadOnlySpan<short> pcm, int sampleRate)
    {
    }

    public void SetListenerGain(float gain)
    {
    }

    public void SetListenerPosition(Vector3 position)
    {
    }

    public void SetListenerOrientation(Vector3 forward, Vector3 up)
    {
    }
}
