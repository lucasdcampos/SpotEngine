using System.Numerics;
using Spot.Core;

namespace Spot.Audio;

/// <summary>
/// A handle to one playing (or finished) sound. Sources are pooled and recycled, so a voice carries the
/// pool generation it was issued at: once the pooled source is reused for another sound the old handle
/// silently stops matching, making stale <see cref="AudioManager.Stop"/> / update calls harmless no-ops.
/// </summary>
public readonly struct Voice
{
    internal Voice(AudioSourceHandle source, int generation)
    {
        Source = source;
        Generation = generation;
    }

    internal AudioSourceHandle Source { get; }

    internal int Generation { get; }

    /// <summary>Gets whether this handle refers to a real source (as opposed to a dropped/failed play).</summary>
    public bool IsValid => Source.IsValid;
}

/// <summary>
/// The engine-wide audio mixer: it owns a fixed pool of backend sources, uploads <see cref="AudioClip"/> PCM
/// into backend buffers on demand, and is the single chokepoint every sound flows through. Playback is issued
/// against an <see cref="IAudioBackend"/> (OpenAL on desktop, Web Audio in the browser). Every method is a
/// no-op when the backend is unavailable, so callers never need to guard for a missing audio device.
/// </summary>
public static class AudioManager
{
    // The backend guarantees only a modest number of simultaneous sources; 32 voices is plenty for a 2D/3D
    // game and stays well under every implementation's limit. Excess simultaneous plays are dropped.
    private const int SourceCount = 32;

    private static IAudioBackend s_backend = new SilentAudioBackend();
    private static AudioSourceHandle[] s_sources = Array.Empty<AudioSourceHandle>();
    private static int[] s_generations = Array.Empty<int>();

    private static bool Available => s_backend.Available;

    /// <summary>
    /// Installs the platform audio backend, opens the device, and allocates the source pool. Safe to call
    /// more than once. Mirrors <see cref="Rendering.Renderer"/>'s device injection: the desktop host passes an
    /// OpenAL backend, the browser host a Web Audio backend.
    /// </summary>
    public static void Init(IAudioBackend backend)
    {
        s_backend = backend ?? new SilentAudioBackend();
        s_backend.Open();
        if (!Available)
        {
            return;
        }

        try
        {
            s_sources = new AudioSourceHandle[SourceCount];
            s_generations = new int[SourceCount];
            for (int i = 0; i < SourceCount; i++)
            {
                s_sources[i] = s_backend.GenSource();
            }
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to allocate audio sources ({0}); audio will run muted.", ex.Message);
            s_sources = Array.Empty<AudioSourceHandle>();
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
                foreach (AudioSourceHandle source in s_sources)
                {
                    s_backend.StopSource(source);
                    s_backend.DeleteSource(source);
                }
            }
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Error while releasing audio sources: {0}", ex.Message);
        }

        s_sources = Array.Empty<AudioSourceHandle>();
        s_generations = Array.Empty<int>();
        s_backend.Close();
        s_backend = new SilentAudioBackend();
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
            s_backend.SetListenerGain(master);
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Audio mix update failed: {0}", ex.Message);
        }
    }

    /// <summary>Positions and orients the 3D listener (typically driven from the active camera transform).</summary>
    public static void SetListener(Vector3 position, Vector3 forward, Vector3 up)
    {
        if (!Available)
        {
            return;
        }

        try
        {
            s_backend.SetListenerPosition(position);
            s_backend.SetListenerOrientation(forward, up);
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
            AudioBufferHandle buffer = EnsureBuffer(clip);
            if (!buffer.IsValid || !TryAcquireSource(out int slot))
            {
                return default;
            }

            AudioSourceHandle source = s_sources[slot];
            s_backend.SetSourceBuffer(source, buffer);
            s_backend.SetSourceGain(source, Math.Max(0.0f, volume));
            s_backend.SetSourcePitch(source, Math.Max(0.01f, pitch));
            s_backend.SetSourceLooping(source, loop);

            if (spatial)
            {
                s_backend.SetSourceRelative(source, false);
                s_backend.SetSourceReferenceDistance(source, Math.Max(0.0f, minDistance));
                s_backend.SetSourceMaxDistance(source, Math.Max(minDistance, maxDistance));
                s_backend.SetSourcePosition(source, position);
            }
            else
            {
                // Anchor to the listener so 2D sounds (UI, music) ignore position entirely.
                s_backend.SetSourceRelative(source, true);
                s_backend.SetSourcePosition(source, Vector3.Zero);
            }

            s_backend.PlaySource(source);
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
            return s_backend.GetSourceState(voice.Source) == AudioSourceState.Playing;
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
            s_backend.StopSource(voice.Source);
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
            try { s_backend.PauseSource(voice.Source); } catch { /* never crash on audio */ }
        }
    }

    /// <summary>Resumes a paused voice.</summary>
    public static void Resume(Voice voice)
    {
        if (IsCurrent(voice))
        {
            try { s_backend.PlaySource(voice.Source); } catch { /* never crash on audio */ }
        }
    }

    /// <summary>Updates the world position of a spatial voice while it plays.</summary>
    public static void SetVoicePosition(Voice voice, Vector3 position)
    {
        if (IsCurrent(voice))
        {
            try { s_backend.SetSourcePosition(voice.Source, position); }
            catch { /* never crash on audio */ }
        }
    }

    /// <summary>Updates the gain (volume) of a voice while it plays.</summary>
    public static void SetVoiceGain(Voice voice, float gain)
    {
        if (IsCurrent(voice))
        {
            try { s_backend.SetSourceGain(voice.Source, Math.Max(0.0f, gain)); }
            catch { /* never crash on audio */ }
        }
    }

    /// <summary>Uploads a clip's PCM into a backend buffer the first time it is played, caching it on the clip.</summary>
    internal static AudioBufferHandle EnsureBuffer(AudioClip clip)
    {
        if (!Available)
        {
            return default;
        }

        if (clip.BackendBuffer != 0)
        {
            return new AudioBufferHandle(clip.BackendBuffer);
        }

        try
        {
            AudioBufferHandle buffer = s_backend.GenBuffer();
            AudioSampleFormat format = clip.Channels >= 2 ? AudioSampleFormat.Stereo16 : AudioSampleFormat.Mono16;
            s_backend.UploadBuffer(buffer, format, clip.Pcm, clip.SampleRate);
            clip.BackendBuffer = buffer.Id;
            return buffer;
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to upload audio buffer: {0}", ex.Message);
            return default;
        }
    }

    /// <summary>Deletes a clip's backend buffer when the clip is disposed.</summary>
    internal static void ReleaseClipBuffer(AudioClip clip)
    {
        if (!Available || clip.BackendBuffer == 0)
        {
            clip.BackendBuffer = 0;
            return;
        }

        try
        {
            var clipBuffer = new AudioBufferHandle(clip.BackendBuffer);

            // A buffer still attached to any source cannot be deleted, so detach it first (stopping the
            // source that holds it). This keeps AudioClip.Dispose safe even mid-playback.
            foreach (AudioSourceHandle source in s_sources)
            {
                if (s_backend.GetSourceBuffer(source) == clipBuffer)
                {
                    s_backend.StopSource(source);
                    s_backend.SetSourceBuffer(source, default);
                }
            }

            s_backend.DeleteBuffer(clipBuffer);
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to release audio buffer: {0}", ex.Message);
        }

        clip.BackendBuffer = 0;
    }

    private static bool TryAcquireSource(out int slot)
    {
        for (int i = 0; i < s_sources.Length; i++)
        {
            AudioSourceState state = s_backend.GetSourceState(s_sources[i]);
            if (state != AudioSourceState.Playing && state != AudioSourceState.Paused)
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
