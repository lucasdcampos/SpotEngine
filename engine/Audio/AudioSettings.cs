namespace Spot.Audio;

/// <summary>
/// Global, engine-wide audio settings. These are runtime mixing knobs that apply to every scene, mirroring
/// <c>RenderSettings</c> for the rendering pipeline. They are deliberately not serialized: they model the
/// player's session (a volume slider, a mute toggle), not authored scene data.
/// </summary>
public static class AudioSettings
{
    /// <summary>Master volume applied to all audio, clamped to the range [0, 1]. Default is full volume.</summary>
    public static float MasterVolume { get; set; } = 1.0f;

    /// <summary>Whether all audio is silenced. Playback state is preserved; only the output is muted.</summary>
    public static bool Muted { get; set; }
}
