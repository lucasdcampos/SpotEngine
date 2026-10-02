using System.Numerics;
using Spot.Framework.Audio;

namespace Spot.Tests.Fakes;

/// <summary>
/// An in-memory <see cref="IAudioBackend"/> that tracks sources and buffers instead of playing sound, so the voice
/// pool, buffer uploads and gain handling can be tested headless. Sources stay "playing" until stopped.
/// </summary>
internal sealed class FakeAudioBackend : IAudioBackend
{
    private uint _nextId = 1;

    public bool OpenSucceeds { get; init; } = true;

    public bool Available { get; private set; }

    public int OpenCount { get; private set; }

    public int CloseCount { get; private set; }

    public Dictionary<uint, Source> Sources { get; } = new();

    public Dictionary<uint, (AudioSampleFormat Format, short[] Pcm, int SampleRate)?> Buffers { get; } = new();

    public float ListenerGain { get; private set; } = 1.0f;

    public Vector3 ListenerPosition { get; private set; }

    public bool Open()
    {
        OpenCount++;
        Available = OpenSucceeds;
        return Available;
    }

    public void Close()
    {
        CloseCount++;
        Available = false;
    }

    public AudioSourceHandle GenSource()
    {
        uint id = _nextId++;
        Sources[id] = new Source();
        return new AudioSourceHandle(id);
    }

    public void DeleteSource(AudioSourceHandle source) => Sources.Remove(source.Id);

    public void PlaySource(AudioSourceHandle source) => Sources[source.Id].State = AudioSourceState.Playing;

    public void StopSource(AudioSourceHandle source) => Sources[source.Id].State = AudioSourceState.Stopped;

    public void PauseSource(AudioSourceHandle source) => Sources[source.Id].State = AudioSourceState.Paused;

    public AudioSourceState GetSourceState(AudioSourceHandle source) => Sources[source.Id].State;

    public void SetSourceBuffer(AudioSourceHandle source, AudioBufferHandle buffer) => Sources[source.Id].Buffer = buffer.Id;

    public AudioBufferHandle GetSourceBuffer(AudioSourceHandle source) => new(Sources[source.Id].Buffer);

    public void SetSourceGain(AudioSourceHandle source, float gain) => Sources[source.Id].Gain = gain;

    public void SetSourcePitch(AudioSourceHandle source, float pitch) => Sources[source.Id].Pitch = pitch;

    public void SetSourceLooping(AudioSourceHandle source, bool looping) => Sources[source.Id].Looping = looping;

    public void SetSourceRelative(AudioSourceHandle source, bool relative) => Sources[source.Id].Relative = relative;

    public void SetSourcePosition(AudioSourceHandle source, Vector3 position) => Sources[source.Id].Position = position;

    public void SetSourceReferenceDistance(AudioSourceHandle source, float distance) => Sources[source.Id].ReferenceDistance = distance;

    public void SetSourceMaxDistance(AudioSourceHandle source, float distance) => Sources[source.Id].MaxDistance = distance;

    public AudioBufferHandle GenBuffer()
    {
        uint id = _nextId++;
        Buffers[id] = null;
        return new AudioBufferHandle(id);
    }

    public void DeleteBuffer(AudioBufferHandle buffer) => Buffers.Remove(buffer.Id);

    public void UploadBuffer(AudioBufferHandle buffer, AudioSampleFormat format, ReadOnlySpan<short> pcm, int sampleRate) =>
        Buffers[buffer.Id] = (format, pcm.ToArray(), sampleRate);

    public void SetListenerGain(float gain) => ListenerGain = gain;

    public void SetListenerPosition(Vector3 position) => ListenerPosition = position;

    public void SetListenerOrientation(Vector3 forward, Vector3 up)
    {
    }

    internal sealed class Source
    {
        public AudioSourceState State { get; set; } = AudioSourceState.Initial;
        public uint Buffer { get; set; }
        public float Gain { get; set; } = 1.0f;
        public float Pitch { get; set; } = 1.0f;
        public bool Looping { get; set; }
        public bool Relative { get; set; }
        public Vector3 Position { get; set; }
        public float ReferenceDistance { get; set; }
        public float MaxDistance { get; set; }
    }
}
