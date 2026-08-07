using System.Numerics;
using Spot.Audio;

namespace Spot.Scenes;

/// <summary>
/// Marks an entity as a sound emitter: it carries what to play (a clip) and how (volume, pitch, looping,
/// 2D vs 3D spatial). It is the audio counterpart to <see cref="Sprite2DComponent"/> — data plus a small
/// control surface for scripts (<see cref="Play"/>/<see cref="Stop"/>). <c>AudioSystem</c> reads it each
/// play-mode frame to honor <see cref="PlayOnAwake"/> and to keep a spatial voice positioned at the
/// entity's transform.
/// </summary>
[ComponentMenu("Audio Source", Order = 60)]
[SceneComponent("AudioSource")]
public sealed class AudioSourceComponent : Component
{
    // Runtime state, never serialized (internal fields, not public properties). AudioSystem keeps
    // WorldPosition fresh each frame so a script's Play() emits from the right place immediately.
    internal Voice CurrentVoice;
    internal Vector3 WorldPosition;
    internal bool AwakeHandled;

    /// <summary>Gets or sets the clip to play. Loaded from <see cref="ClipPath"/> when a scene is deserialized.</summary>
    [AssetReference(nameof(ClipPath))]
    public AudioClip? Clip { get; set; }

    /// <summary>Gets or sets the stored reference to the clip, used for serialization.</summary>
    [HideInInspector]
    public string? ClipPath { get; set; }

    /// <summary>Gets or sets the linear volume, where 1 is the clip's authored level.</summary>
    [InspectorRange(0.0f, 1.0f, 0.01f)]
    public float Volume { get; set; } = 1.0f;

    /// <summary>Gets or sets the playback pitch/speed multiplier, where 1 is the original.</summary>
    [InspectorRange(0.1f, 3.0f, 0.01f)]
    public float Pitch { get; set; } = 1.0f;

    /// <summary>Gets or sets whether playback loops until stopped.</summary>
    public bool Loop { get; set; }

    /// <summary>Gets or sets whether the clip plays automatically when the scene starts running.</summary>
    public bool PlayOnAwake { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the sound is positioned in 3D and attenuates with distance to the listener. When
    /// off, it plays flat (2D) — the right choice for UI and music.
    /// </summary>
    public bool Spatial { get; set; } = true;

    /// <summary>Gets or sets the distance within which a spatial sound plays at full volume.</summary>
    [ShowIf(nameof(Spatial), true)]
    [InspectorRange(0.0f, 100.0f, 0.1f)]
    public float MinDistance { get; set; } = 1.0f;

    /// <summary>Gets or sets the distance beyond which a spatial sound no longer attenuates further.</summary>
    [ShowIf(nameof(Spatial), true)]
    [InspectorRange(1.0f, 1000.0f, 1.0f)]
    public float MaxDistance { get; set; } = 100.0f;

    /// <summary>Gets whether this source's voice is currently playing.</summary>
    public bool IsPlaying => AudioManager.IsPlaying(CurrentVoice);

    /// <summary>Starts (or restarts) playback of <see cref="Clip"/> using the current settings.</summary>
    public void Play()
    {
        Stop();
        CurrentVoice = AudioManager.Play(Clip, Volume, Pitch, Loop, Spatial, WorldPosition, MinDistance, MaxDistance);
    }

    /// <summary>Stops playback if the source is playing.</summary>
    public void Stop()
    {
        AudioManager.Stop(CurrentVoice);
        CurrentVoice = default;
    }

    /// <summary>Pauses playback, preserving position; resume with <see cref="Resume"/>.</summary>
    public void Pause() => AudioManager.Pause(CurrentVoice);

    /// <summary>Resumes playback after <see cref="Pause"/>.</summary>
    public void Resume() => AudioManager.Resume(CurrentVoice);
}
