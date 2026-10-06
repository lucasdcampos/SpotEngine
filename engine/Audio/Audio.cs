using System.Numerics;

namespace Spot.Engine.Audio;

/// <summary>
/// The simple, fire-and-forget audio API for game scripts. For one-shot sounds that need no follow-up
/// control (a jump, a pickup, a UI click) these one-liners are all a script needs; for looping music or a
/// sound you later stop or reposition, drive an <c>AudioSourceComponent</c> instead, which holds its own
/// <see cref="Voice"/>. Both route through <see cref="AudioManager"/>, so both are safe no-ops when there
/// is no audio device.
/// </summary>
public static class Audio
{
    /// <summary>Plays a clip flat (2D), unaffected by listener position — ideal for UI and music stingers.</summary>
    /// <param name="clip">The clip to play; a null clip is ignored.</param>
    /// <param name="volume">Linear gain, where 1 is the clip's authored level.</param>
    /// <param name="pitch">Playback pitch/speed multiplier, where 1 is the original.</param>
    /// <param name="bus">
    /// The <see cref="AudioMixer"/> bus to route through; null uses <see cref="AudioMixer.SfxBus"/>. Pass
    /// <see cref="AudioMixer.UiBus"/> or <see cref="AudioMixer.MusicBus"/> so the player's group volumes apply.
    /// </param>
    public static Voice Play(AudioClip? clip, float volume = 1.0f, float pitch = 1.0f, string? bus = null)
        => AudioManager.Play(clip, volume, pitch, loop: false, spatial: false, bus: bus ?? AudioMixer.SfxBus);

    /// <summary>Plays a clip at a world position (3D), attenuating with distance to the listener.</summary>
    /// <param name="clip">The clip to play; a null clip is ignored.</param>
    /// <param name="worldPosition">The world-space position to emit the sound from.</param>
    /// <param name="volume">Linear gain, where 1 is the clip's authored level.</param>
    /// <param name="pitch">Playback pitch/speed multiplier, where 1 is the original.</param>
    /// <param name="bus">The <see cref="AudioMixer"/> bus to route through; null uses <see cref="AudioMixer.SfxBus"/>.</param>
    public static Voice PlayAt(AudioClip? clip, Vector3 worldPosition, float volume = 1.0f, float pitch = 1.0f,
        string? bus = null)
        => AudioManager.Play(clip, volume, pitch, loop: false, spatial: true, position: worldPosition,
            bus: bus ?? AudioMixer.SfxBus);
}
