using System.Collections.Generic;
using System.Linq;
using Spot.Framework.Audio;
using Xunit;

namespace Spot.Framework.Tests;

/// <summary>
/// Covers the mixer's routing maths — the gain a sound ends up with after its bus chain, mutes, and solos — plus
/// the layout edits the editor drives and the project round-trip. The mixer is engine-global state, so every
/// test starts from the default layout.
/// </summary>
public class AudioMixerTests
{
    public AudioMixerTests() => AudioMixer.ResetToDefaults();

    [Fact]
    public void Defaults_ProvideMasterMusicSfxAndUi()
    {
        Assert.Equal(new[] { "Master", "Music", "SFX", "UI" }, AudioMixer.BusNames);
        Assert.All(AudioMixer.Buses, bus => Assert.Equal(1.0f, bus.Volume));
        Assert.Equal("Master", Assert.IsType<AudioBus>(AudioMixer.Find("music")).Parent);
    }

    [Fact]
    public void GetGain_MultipliesTheBusChain()
    {
        AudioMixer.Master.Volume = 0.5f;
        AudioMixer.Find("Music")!.Volume = 0.4f;

        Assert.Equal(0.2f, AudioMixer.GetGain("Music"), 4);
        Assert.Equal(0.5f, AudioMixer.GetGain("SFX"), 4);
    }

    [Fact]
    public void GetGain_UnknownOrEmptyBus_FallsBackToMaster()
    {
        AudioMixer.Master.Volume = 0.25f;

        Assert.Equal(0.25f, AudioMixer.GetGain("Ambience"), 4);
        Assert.Equal(0.25f, AudioMixer.GetGain(null), 4);
    }

    [Fact]
    public void GetGain_MutedAncestor_SilencesDescendants()
    {
        AudioBus group = AudioMixer.AddBus("Weapons", "SFX");
        AudioMixer.Find("SFX")!.Mute = true;

        Assert.Equal(0.0f, AudioMixer.GetGain(group.Name));
        Assert.Equal(1.0f, AudioMixer.GetGain("Music"), 4);
    }

    [Fact]
    public void Solo_KeepsTheSoloedBranchAndItsAncestorsAudible()
    {
        AudioBus loops = AudioMixer.AddBus("Loops", "Music");
        AudioMixer.Find("Music")!.Solo = true;

        Assert.True(AudioMixer.AnySolo);
        Assert.Equal(1.0f, AudioMixer.GetGain("Music"), 4);
        Assert.Equal(1.0f, AudioMixer.GetGain(loops.Name), 4);   // under the soloed bus
        Assert.Equal(1.0f, AudioMixer.GetGain("Master"), 4);     // carries the signal out
        Assert.Equal(0.0f, AudioMixer.GetGain("SFX"));           // unrelated branch

        AudioMixer.ClearSolos();
        Assert.False(AudioMixer.AnySolo);
        Assert.Equal(1.0f, AudioMixer.GetGain("SFX"), 4);
    }

    [Fact]
    public void Volume_IsClampedToUnitRange()
    {
        AudioMixer.Master.Volume = 5.0f;
        Assert.Equal(1.0f, AudioMixer.Master.Volume);

        AudioMixer.Master.Volume = -2.0f;
        Assert.Equal(0.0f, AudioMixer.Master.Volume);
    }

    [Fact]
    public void AudioSettings_MirrorsTheMasterBus()
    {
        AudioSettings.MasterVolume = 0.3f;
        Assert.Equal(0.3f, AudioMixer.Master.Volume, 4);

        AudioMixer.Master.Mute = true;
        Assert.True(AudioSettings.Muted);

        AudioSettings.SetBusVolume("SFX", 0.6f);
        Assert.Equal(0.6f, AudioSettings.GetBusVolume("SFX"), 4);
        Assert.Equal(0.0f, AudioSettings.GetBusVolume("Nope"));
    }

    [Fact]
    public void AddBus_UniquifiesNamesAndFallsBackToMasterForAnUnknownParent()
    {
        AudioBus first = AudioMixer.AddBus("Music");
        AudioBus second = AudioMixer.AddBus("Ambience", "Nowhere");

        Assert.Equal("Music 2", first.Name);
        Assert.Equal("Master", second.Parent);
    }

    [Fact]
    public void RemoveBus_ReparentsChildrenAndProtectsMaster()
    {
        AudioMixer.AddBus("Weapons", "SFX");

        Assert.True(AudioMixer.RemoveBus("SFX"));
        Assert.Null(AudioMixer.Find("SFX"));
        Assert.Equal("Master", AudioMixer.Find("Weapons")!.Parent);

        Assert.False(AudioMixer.RemoveBus("Master"));
        Assert.NotNull(AudioMixer.Find("Master"));
    }

    [Fact]
    public void RenameBus_RepointsChildrenAndProtectsMaster()
    {
        AudioMixer.AddBus("Weapons", "SFX");

        Assert.True(AudioMixer.RenameBus("SFX", "Effects"));
        Assert.Equal("Effects", AudioMixer.Find("Weapons")!.Parent);
        Assert.Null(AudioMixer.Find("SFX"));

        Assert.False(AudioMixer.RenameBus("Master", "Out"));
        Assert.False(AudioMixer.RenameBus("Effects", "   "));
    }

    [Fact]
    public void Reparent_RejectsCyclesAndMaster()
    {
        AudioMixer.AddBus("Weapons", "SFX");

        Assert.False(AudioMixer.Reparent("SFX", "Weapons"));  // would make SFX its own descendant
        Assert.False(AudioMixer.Reparent("SFX", "SFX"));
        Assert.False(AudioMixer.Reparent("Master", "Music"));
        Assert.True(AudioMixer.Reparent("Weapons", "Music"));
        Assert.Equal("Music", AudioMixer.Find("Weapons")!.Parent);
    }

    [Fact]
    public void Layout_RoundTripsThroughGetAndSet()
    {
        AudioMixer.Find("Music")!.Volume = 0.35f;
        AudioMixer.AddBus("Weapons", "SFX").Mute = true;
        List<AudioBusDefinition> saved = AudioMixer.GetLayout();

        AudioMixer.ResetToDefaults();
        AudioMixer.SetLayout(saved);

        Assert.Equal(new[] { "Master", "Music", "SFX", "UI", "Weapons" }, AudioMixer.BusNames.OrderBy(n => n).ToArray());
        Assert.Equal(0.35f, AudioMixer.Find("Music")!.Volume, 4);
        Assert.Equal("SFX", AudioMixer.Find("Weapons")!.Parent);
        Assert.True(AudioMixer.Find("Weapons")!.Mute);
    }

    [Fact]
    public void SetLayout_RepairsAMalformedLayout()
    {
        // No master, an unknown parent, and a two-bus parent cycle: all of it has to load without throwing and
        // leave every bus reachable from the root.
        AudioMixer.SetLayout(new List<AudioBusDefinition>
        {
            new() { Name = "Music", Parent = "Ghost", Volume = 0.5f },
            new() { Name = "A", Parent = "B" },
            new() { Name = "B", Parent = "A" },
        });

        Assert.NotNull(AudioMixer.Find("Master"));
        Assert.Equal(0.5f, AudioMixer.Find("Music")!.Volume, 4);
        Assert.All(AudioMixer.Buses, bus => Assert.True(Reaches(bus, AudioMixer.Master)));
    }

    [Fact]
    public void SetLayout_EmptyOrNull_RestoresDefaults()
    {
        AudioMixer.SetLayout(new List<AudioBusDefinition>());
        Assert.Equal(new[] { "Master", "Music", "SFX", "UI" }, AudioMixer.BusNames);

        AudioMixer.AddBus("Temp");
        AudioMixer.SetLayout(null);
        Assert.Equal(new[] { "Master", "Music", "SFX", "UI" }, AudioMixer.BusNames);
    }

    // Walks a bus's parents, bounded, to confirm the layout actually terminates at the root.
    private static bool Reaches(AudioBus bus, AudioBus root)
    {
        AudioBus? node = bus;
        for (int depth = 0; node is not null && depth < 32; depth++)
        {
            if (ReferenceEquals(node, root))
            {
                return true;
            }

            node = AudioMixer.ParentOf(node);
        }

        return false;
    }
}
