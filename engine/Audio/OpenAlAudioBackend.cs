using System;
using System.Numerics;
using Silk.NET.OpenAL;

namespace Spot.Audio;

/// <summary>
/// The desktop <see cref="IAudioBackend"/>: maps the neutral source/buffer/listener operations onto
/// Silk.NET's OpenAL bindings. Device ownership (opening the default device and making a context current)
/// is delegated to <see cref="AudioDevice"/>. This type is deliberately thin — it performs raw OpenAL
/// calls and lets <see cref="AudioManager"/> own pooling and the never-crash guards.
/// </summary>
internal sealed unsafe class OpenAlAudioBackend : IAudioBackend
{
    private readonly float[] _orientation = new float[6];

    private static AL Al => AudioDevice.Al!;

    public bool Available => AudioDevice.Available;

    public bool Open()
    {
        AudioDevice.Open();
        return AudioDevice.Available;
    }

    public void Close() => AudioDevice.Close();

    public AudioSourceHandle GenSource() => new(Al.GenSource());

    public void DeleteSource(AudioSourceHandle source) => Al.DeleteSource(source.Id);

    public void PlaySource(AudioSourceHandle source) => Al.SourcePlay(source.Id);

    public void StopSource(AudioSourceHandle source) => Al.SourceStop(source.Id);

    public void PauseSource(AudioSourceHandle source) => Al.SourcePause(source.Id);

    public AudioSourceState GetSourceState(AudioSourceHandle source)
    {
        Al.GetSourceProperty(source.Id, GetSourceInteger.SourceState, out int state);
        return (SourceState)state switch
        {
            SourceState.Playing => AudioSourceState.Playing,
            SourceState.Paused => AudioSourceState.Paused,
            SourceState.Stopped => AudioSourceState.Stopped,
            _ => AudioSourceState.Initial,
        };
    }

    public void SetSourceBuffer(AudioSourceHandle source, AudioBufferHandle buffer) =>
        Al.SetSourceProperty(source.Id, SourceInteger.Buffer, (int)buffer.Id);

    public AudioBufferHandle GetSourceBuffer(AudioSourceHandle source)
    {
        Al.GetSourceProperty(source.Id, GetSourceInteger.Buffer, out int bound);
        return new AudioBufferHandle((uint)bound);
    }

    public void SetSourceGain(AudioSourceHandle source, float gain) =>
        Al.SetSourceProperty(source.Id, SourceFloat.Gain, gain);

    public void SetSourcePitch(AudioSourceHandle source, float pitch) =>
        Al.SetSourceProperty(source.Id, SourceFloat.Pitch, pitch);

    public void SetSourceLooping(AudioSourceHandle source, bool looping) =>
        Al.SetSourceProperty(source.Id, SourceBoolean.Looping, looping);

    public void SetSourceRelative(AudioSourceHandle source, bool relative) =>
        Al.SetSourceProperty(source.Id, SourceBoolean.SourceRelative, relative);

    public void SetSourcePosition(AudioSourceHandle source, Vector3 position) =>
        Al.SetSourceProperty(source.Id, SourceVector3.Position, position.X, position.Y, position.Z);

    public void SetSourceReferenceDistance(AudioSourceHandle source, float distance) =>
        Al.SetSourceProperty(source.Id, SourceFloat.ReferenceDistance, distance);

    public void SetSourceMaxDistance(AudioSourceHandle source, float distance) =>
        Al.SetSourceProperty(source.Id, SourceFloat.MaxDistance, distance);

    public AudioBufferHandle GenBuffer() => new(Al.GenBuffer());

    public void DeleteBuffer(AudioBufferHandle buffer) => Al.DeleteBuffer(buffer.Id);

    public void UploadBuffer(AudioBufferHandle buffer, AudioSampleFormat format, ReadOnlySpan<short> pcm, int sampleRate)
    {
        BufferFormat alFormat = format == AudioSampleFormat.Stereo16 ? BufferFormat.Stereo16 : BufferFormat.Mono16;
        fixed (short* p = pcm)
        {
            Al.BufferData(buffer.Id, alFormat, p, pcm.Length * sizeof(short), sampleRate);
        }
    }

    public void SetListenerGain(float gain) => Al.SetListenerProperty(ListenerFloat.Gain, gain);

    public void SetListenerPosition(Vector3 position) =>
        Al.SetListenerProperty(ListenerVector3.Position, position.X, position.Y, position.Z);

    public void SetListenerOrientation(Vector3 forward, Vector3 up)
    {
        _orientation[0] = forward.X;
        _orientation[1] = forward.Y;
        _orientation[2] = forward.Z;
        _orientation[3] = up.X;
        _orientation[4] = up.Y;
        _orientation[5] = up.Z;
        fixed (float* p = _orientation)
        {
            Al.SetListenerProperty(ListenerFloatArray.Orientation, p);
        }
    }
}
