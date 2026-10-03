using System.Numerics;
using Spot.Framework.Audio;
using Spot.Tests.Fakes;

namespace Spot.Framework.Tests;

/// <summary>
/// Covers the framework's voice manager on top of an in-memory audio backend: the source pool, buffer uploads,
/// voice handles, bus gain, and degrading to silence.
/// </summary>
public class AudioManagerTests : IDisposable
{
    private readonly FakeAudioBackend _backend = new();

    public AudioManagerTests()
    {
        AudioMixer.ResetToDefaults();
        AudioManager.Init(_backend);
    }

    public void Dispose()
    {
        AudioManager.Shutdown();
        AudioMixer.ResetToDefaults();
    }

    private static AudioClip Clip(int channels = 1) => new(new short[] { 1, 2, 3, 4 }, channels, 8000);

    [Fact]
    public void Init_OpensTheDeviceAndAllocatesThePool()
    {
        Assert.True(AudioManager.IsAvailable);
        Assert.Equal(1, _backend.OpenCount);
        Assert.Equal(32, _backend.Sources.Count);
    }

    [Fact]
    public void Play_UploadsTheClipOnceAndConfiguresTheSource()
    {
        AudioClip clip = Clip(channels: 2);

        Voice first = AudioManager.Play(clip, volume: 0.5f, pitch: 1.5f, loop: true);
        AudioManager.Play(clip);

        Assert.True(first.IsValid);
        var (format, pcm, rate) = Assert.Single(_backend.Buffers.Values)!.Value;
        Assert.Equal((AudioSampleFormat.Stereo16, 8000), (format, rate));
        Assert.Equal(clip.Pcm, pcm);

        FakeAudioBackend.Source source = _backend.Sources.Values.First(s => s.Looping);
        Assert.Equal(AudioSourceState.Playing, source.State);
        Assert.Equal(1.5f, source.Pitch);
        Assert.Equal(0.5f, source.Gain, 4);
        Assert.True(source.Relative); // non-spatial sounds follow the listener
    }

    [Fact]
    public void Play_SpatialSoundsArePlacedInTheWorld()
    {
        AudioManager.Play(Clip(), spatial: true, position: new Vector3(1, 2, 3), minDistance: 2f, maxDistance: 1f);

        FakeAudioBackend.Source source = _backend.Sources.Values.Single(s => s.State == AudioSourceState.Playing);
        Assert.False(source.Relative);
        Assert.Equal(new Vector3(1, 2, 3), source.Position);
        Assert.Equal(2f, source.ReferenceDistance);
        Assert.Equal(2f, source.MaxDistance); // never below the reference distance
    }

    [Fact]
    public void Voices_StopPauseAndResume()
    {
        Voice voice = AudioManager.Play(Clip());
        Assert.True(AudioManager.IsPlaying(voice));

        AudioManager.Pause(voice);
        Assert.False(AudioManager.IsPlaying(voice));

        AudioManager.Resume(voice);
        Assert.True(AudioManager.IsPlaying(voice));

        AudioManager.Stop(voice);
        Assert.False(AudioManager.IsPlaying(voice));
    }

    [Fact]
    public void StaleVoices_AreIgnoredOnceTheirSourceIsReused()
    {
        // Fill the pool, stop one voice, and play again: the freed source gets a new generation.
        var voices = Enumerable.Range(0, 32).Select(_ => AudioManager.Play(Clip())).ToList();
        AudioManager.Stop(voices[5]);
        Voice reused = AudioManager.Play(Clip());

        Assert.True(reused.IsValid);
        AudioManager.Stop(voices[5]); // the old handle must not stop the new sound
        Assert.True(AudioManager.IsPlaying(reused));
    }

    [Fact]
    public void Play_WhenEverySourceIsBusy_DropsTheSound()
    {
        for (int i = 0; i < 32; i++)
        {
            AudioManager.Play(Clip());
        }

        Assert.False(AudioManager.Play(Clip()).IsValid);
    }

    [Fact]
    public void Gain_CombinesTheVoiceVolumeWithItsBus()
    {
        AudioMixer.Find(AudioMixer.SfxBus)!.Volume = 0.5f;
        Voice voice = AudioManager.Play(Clip(), volume: 0.8f, bus: AudioMixer.SfxBus);

        Assert.Equal(0.4f, AudioManager.GetVoiceGain(voice), 4);

        AudioManager.SetVoiceGain(voice, 0.2f);
        Assert.Equal(0.1f, AudioManager.GetVoiceGain(voice), 4);

        AudioMixer.Find(AudioMixer.SfxBus)!.Mute = true;
        AudioManager.Update(0.016f);
        Assert.Equal(0f, _backend.Sources.Values.Single(s => s.State == AudioSourceState.Playing).Gain);
    }

    [Fact]
    public void DisposingAClip_DetachesItFromSourcesAndFreesItsBuffer()
    {
        AudioClip clip = Clip();
        AudioManager.Play(clip);
        Assert.Single(_backend.Buffers);

        clip.Dispose();

        Assert.Empty(_backend.Buffers);
        Assert.DoesNotContain(_backend.Sources.Values, s => s.State == AudioSourceState.Playing);
    }

    [Fact]
    public void NullClip_PlaysNothing()
    {
        Assert.False(AudioManager.Play(null).IsValid);
    }

    [Fact]
    public void Shutdown_ReleasesTheSourcesAndClosesTheDevice()
    {
        AudioManager.Shutdown();

        Assert.Empty(_backend.Sources);
        Assert.Equal(1, _backend.CloseCount);
        Assert.False(AudioManager.IsAvailable);
    }

    [Fact]
    public void AnUnavailableDevice_RunsSilently()
    {
        AudioManager.Shutdown();
        var broken = new FakeAudioBackend { OpenSucceeds = false };

        AudioManager.Init(broken);

        Assert.False(AudioManager.IsAvailable);
        Assert.Empty(broken.Sources);
        Assert.False(AudioManager.Play(Clip()).IsValid);
    }

    [Fact]
    public void DefaultBackend_IsOpenAlOnDesktop()
    {
        Assert.IsType<OpenAlAudioBackend>(AudioManager.CreateDefaultBackend());
    }
}
