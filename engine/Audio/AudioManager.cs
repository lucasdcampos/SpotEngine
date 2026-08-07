using System.Numerics;
using Silk.NET.OpenAL;
using Spot.Core;

namespace Spot.Audio;

/// <summary>
/// A handle to one playing (or finished) sound. Sources are pooled and recycled, so a voice carries the
/// pool generation it was issued at: once the pooled source is reused for another sound the old handle
/// silently stops matching, making stale <see cref="AudioManager.Stop"/> / update calls harmless no-ops.
/// </summary>
public readonly struct Voice
{
    internal Voice(uint source, int generation)
    {
        Source = source;
        Generation = generation;
    }

    internal uint Source { get; }

    internal int Generation { get; }

    /// <summary>Gets whether this handle refers to a real source (as opposed to a dropped/failed play).</summary>
    public bool IsValid => Source != 0;
}

/// <summary>
/// The engine-wide audio mixer: it owns a fixed pool of OpenAL sources, uploads <see cref="AudioClip"/> PCM
/// into OpenAL buffers on demand, and is the single chokepoint every sound flows through. Every method is
/// a no-op when <see cref="AudioDevice"/> is unavailable, so callers never need to guard for a missing
/// audio device.
/// </summary>
public static class AudioManager
{
    // OpenAL guarantees only a modest number of simultaneous sources; 32 voices is plenty for a 2D/3D
    // game and stays well under every implementation's limit. Excess simultaneous plays are dropped.
    private const int SourceCount = 32;

    private static uint[] s_sources = Array.Empty<uint>();
    private static int[] s_generations = Array.Empty<int>();
    private static readonly float[] s_orientation = new float[6];

    private static AL? Al => AudioDevice.Al;

    private static bool Available => AudioDevice.Available;

    /// <summary>Opens the audio device and allocates the source pool. Safe to call more than once.</summary>
    public static void Init()
    {
        AudioDevice.Open();
        if (!Available)
        {
            return;
        }

        try
        {
            s_sources = new uint[SourceCount];
            s_generations = new int[SourceCount];
            AL al = Al!;
            for (int i = 0; i < SourceCount; i++)
            {
                s_sources[i] = al.GenSource();
            }
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to allocate audio sources ({0}); audio will run muted.", ex.Message);
            s_sources = Array.Empty<uint>();
            s_generations = Array.Empty<int>();
        }
    }

    /// <summary>Releases the source pool and closes the device.</summary>
    public static void Shutdown()
    {
        try
        {
            if (Available && s_sources.Length > 0)
            {
                AL al = Al!;
                foreach (uint source in s_sources)
                {
                    al.SourceStop(source);
                    al.DeleteSource(source);
                }
            }
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Error while releasing audio sources: {0}", ex.Message);
        }

        s_sources = Array.Empty<uint>();
        s_generations = Array.Empty<int>();
        AudioDevice.Close();
    }

    /// <summary>Applies the global mix (master volume / mute) to the listener. Called once per frame.</summary>
    public static void Update(float deltaTime)
    {
        _ = deltaTime;
        if (!Available)
        {
            return;
        }

        try
        {
            float master = AudioSettings.Muted ? 0.0f : Math.Clamp(AudioSettings.MasterVolume, 0.0f, 1.0f);
            Al!.SetListenerProperty(ListenerFloat.Gain, master);
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Audio mix update failed: {0}", ex.Message);
        }
    }

    /// <summary>Positions and orients the 3D listener (typically driven from the active camera transform).</summary>
    public static unsafe void SetListener(Vector3 position, Vector3 forward, Vector3 up)
    {
        if (!Available)
        {
            return;
        }

        try
        {
            AL al = Al!;
            al.SetListenerProperty(ListenerVector3.Position, position.X, position.Y, position.Z);
            s_orientation[0] = forward.X;
            s_orientation[1] = forward.Y;
            s_orientation[2] = forward.Z;
            s_orientation[3] = up.X;
            s_orientation[4] = up.Y;
            s_orientation[5] = up.Z;
            fixed (float* p = s_orientation)
            {
                al.SetListenerProperty(ListenerFloatArray.Orientation, p);
            }
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to set audio listener: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Plays a clip on a free pooled source. A spatial voice is positioned in world space and attenuates with
    /// distance to the listener; a non-spatial voice plays flat (UI, music). Returns an invalid <see cref="Voice"/>
    /// when audio is unavailable, the clip is null, or every source is busy — never throwing.
    /// </summary>
    public static Voice Play(AudioClip? clip, float volume = 1.0f, float pitch = 1.0f, bool loop = false,
        bool spatial = false, Vector3 position = default, float minDistance = 1.0f, float maxDistance = 100.0f)
    {
        if (!Available || clip is null)
        {
            return default;
        }

        try
        {
            uint buffer = EnsureBuffer(clip);
            if (buffer == 0 || !TryAcquireSource(out int slot))
            {
                return default;
            }

            AL al = Al!;
            uint source = s_sources[slot];
            al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);
            al.SetSourceProperty(source, SourceFloat.Gain, Math.Max(0.0f, volume));
            al.SetSourceProperty(source, SourceFloat.Pitch, Math.Max(0.01f, pitch));
            al.SetSourceProperty(source, SourceBoolean.Looping, loop);

            if (spatial)
            {
                al.SetSourceProperty(source, SourceBoolean.SourceRelative, false);
                al.SetSourceProperty(source, SourceFloat.ReferenceDistance, Math.Max(0.0f, minDistance));
                al.SetSourceProperty(source, SourceFloat.MaxDistance, Math.Max(minDistance, maxDistance));
                al.SetSourceProperty(source, SourceVector3.Position, position.X, position.Y, position.Z);
            }
            else
            {
                // Anchor to the listener so 2D sounds (UI, music) ignore position entirely.
                al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
                al.SetSourceProperty(source, SourceVector3.Position, 0.0f, 0.0f, 0.0f);
            }

            al.SourcePlay(source);
            return new Voice(source, s_generations[slot]);
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to play audio clip: {0}", ex.Message);
            return default;
        }
    }

    /// <summary>Gets whether the voice's source is still the one it was issued for and is currently playing.</summary>
    public static bool IsPlaying(Voice voice)
    {
        if (!IsCurrent(voice))
        {
            return false;
        }

        try
        {
            Al!.GetSourceProperty(voice.Source, GetSourceInteger.SourceState, out int state);
            return state == (int)SourceState.Playing;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Stops a voice if it is still current; a stale handle is ignored.</summary>
    public static void Stop(Voice voice)
    {
        if (!IsCurrent(voice))
        {
            return;
        }

        try
        {
            Al!.SourceStop(voice.Source);
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to stop audio voice: {0}", ex.Message);
        }
    }

    /// <summary>Pauses a currently-playing voice, preserving its playback position.</summary>
    public static void Pause(Voice voice)
    {
        if (IsCurrent(voice))
        {
            try { Al!.SourcePause(voice.Source); } catch { /* never crash on audio */ }
        }
    }

    /// <summary>Resumes a paused voice.</summary>
    public static void Resume(Voice voice)
    {
        if (IsCurrent(voice))
        {
            try { Al!.SourcePlay(voice.Source); } catch { /* never crash on audio */ }
        }
    }

    /// <summary>Updates the world position of a spatial voice while it plays.</summary>
    public static void SetVoicePosition(Voice voice, Vector3 position)
    {
        if (IsCurrent(voice))
        {
            try { Al!.SetSourceProperty(voice.Source, SourceVector3.Position, position.X, position.Y, position.Z); }
            catch { /* never crash on audio */ }
        }
    }

    /// <summary>Updates the gain (volume) of a voice while it plays.</summary>
    public static void SetVoiceGain(Voice voice, float gain)
    {
        if (IsCurrent(voice))
        {
            try { Al!.SetSourceProperty(voice.Source, SourceFloat.Gain, Math.Max(0.0f, gain)); }
            catch { /* never crash on audio */ }
        }
    }

    /// <summary>Uploads a clip's PCM into an OpenAL buffer the first time it is played, caching it on the clip.</summary>
    internal static uint EnsureBuffer(AudioClip clip)
    {
        if (!Available)
        {
            return 0;
        }

        if (clip.AlBuffer != 0)
        {
            return clip.AlBuffer;
        }

        try
        {
            AL al = Al!;
            uint buffer = al.GenBuffer();
            BufferFormat format = clip.Channels >= 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;
            al.BufferData<short>(buffer, format, clip.Pcm, clip.SampleRate);
            clip.AlBuffer = buffer;
            return buffer;
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to upload audio buffer: {0}", ex.Message);
            return 0;
        }
    }

    /// <summary>Deletes a clip's OpenAL buffer when the clip is disposed.</summary>
    internal static void ReleaseClipBuffer(AudioClip clip)
    {
        if (!Available || clip.AlBuffer == 0)
        {
            clip.AlBuffer = 0;
            return;
        }

        try
        {
            AL al = Al!;

            // A buffer still attached to any source cannot be deleted, so detach it first (stopping the
            // source that holds it). This keeps AudioClip.Dispose safe even mid-playback.
            foreach (uint source in s_sources)
            {
                al.GetSourceProperty(source, GetSourceInteger.Buffer, out int bound);
                if ((uint)bound == clip.AlBuffer)
                {
                    al.SourceStop(source);
                    al.SetSourceProperty(source, SourceInteger.Buffer, 0);
                }
            }

            al.DeleteBuffer(clip.AlBuffer);
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to release audio buffer: {0}", ex.Message);
        }

        clip.AlBuffer = 0;
    }

    private static bool TryAcquireSource(out int slot)
    {
        AL al = Al!;
        for (int i = 0; i < s_sources.Length; i++)
        {
            al.GetSourceProperty(s_sources[i], GetSourceInteger.SourceState, out int state);
            if (state != (int)SourceState.Playing && state != (int)SourceState.Paused)
            {
                s_generations[i]++;
                slot = i;
                return true;
            }
        }

        slot = -1;
        return false;
    }

    private static bool IsCurrent(Voice voice)
    {
        if (!Available || !voice.IsValid)
        {
            return false;
        }

        int slot = Array.IndexOf(s_sources, voice.Source);
        return slot >= 0 && s_generations[slot] == voice.Generation;
    }
}
