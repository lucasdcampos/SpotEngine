namespace Spot.Framework.Audio;

/// <summary>
/// Global, engine-wide audio settings. These are runtime mixing knobs that apply to every scene, mirroring
/// <c>RenderSettings</c> for the rendering pipeline. They are deliberately not serialized: they model the
/// player's session (a volume slider, a mute toggle), not authored scene data.
/// </summary>
/// <remarks>
/// Master volume and mute are the <see cref="AudioMixer"/>'s root bus, surfaced here under the names a game's
/// options menu expects. There is one source of truth: moving this slider moves the Master fader in the mixer,
/// and vice versa. Per-group levels (music, SFX, UI) live on their own buses — see <see cref="AudioMixer"/>.
/// </remarks>
public static class AudioSettings
{
    /// <summary>Master volume applied to all audio, clamped to the range [0, 1]. Default is full volume.</summary>
    public static float MasterVolume
    {
        get => AudioMixer.Master.Volume;
        set => AudioMixer.Master.Volume = value;
    }

    /// <summary>Whether all audio is silenced. Playback state is preserved; only the output is muted.</summary>
    public static bool Muted
    {
        get => AudioMixer.Master.Mute;
        set => AudioMixer.Master.Mute = value;
    }

    /// <summary>
    /// Gets or sets the volume of a named mixer bus (for example "Music" or "SFX"), clamped to [0, 1]. Reading
    /// an unknown bus returns 0 and setting one is ignored, so an options menu can offer a slider for a group
    /// the project may not define.
    /// </summary>
    public static float GetBusVolume(string bus) => AudioMixer.Find(bus)?.Volume ?? 0.0f;

    /// <summary>Sets the volume of a named mixer bus, ignoring an unknown name.</summary>
    public static void SetBusVolume(string bus, float volume)
    {
        AudioBus? target = AudioMixer.Find(bus);
        if (target is not null)
        {
            target.Volume = volume;
        }
    }
}
