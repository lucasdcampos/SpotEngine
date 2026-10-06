using Spot.Engine;

namespace Spot.Engine.Audio;

/// <summary>
/// The engine's bus tree: named volume groups ("Music", "SFX", "UI" under "Master") that every sound is routed
/// through by name. It answers one question for <see cref="AudioManager"/> — what gain applies to a sound on
/// bus X right now — by walking that bus's ancestors and folding in their faders, mutes, and solos. Layout is
/// authored per project (<c>ApplicationSpec.AudioBuses</c>) and applied at startup; levels are a runtime knob,
/// like <see cref="AudioSettings"/>.
///
/// The mixer never throws and never rejects a sound: a missing or misspelled bus name falls back to
/// <see cref="MasterBus"/>, so a stale scene reference is quiet-but-audible rather than silent or fatal.
/// </summary>
public static class AudioMixer
{
    /// <summary>The name of the root bus, through which all audio passes.</summary>
    public const string MasterBus = "Master";

    /// <summary>The name of the default bus for music.</summary>
    public const string MusicBus = "Music";

    /// <summary>The name of the default bus for sound effects, and the default for new audio sources.</summary>
    public const string SfxBus = "SFX";

    /// <summary>The name of the default bus for interface sounds.</summary>
    public const string UiBus = "UI";

    // Guards against a malformed layout (a parent cycle) turning a gain query into an infinite walk.
    private const int MaxDepth = 16;

    // How much of a bus meter survives one second of silence; meters rise instantly and fall smoothly.
    private const float MeterDecayPerSecond = 0.08f;

    private static readonly List<AudioBus> s_buses = new();
    private static readonly Dictionary<string, AudioBus> s_byName = new(StringComparer.OrdinalIgnoreCase);
    private static string[]? s_names;

    static AudioMixer()
    {
        ResetToDefaults();
    }

    /// <summary>Gets every bus, the root first (the order the editor draws channel strips in).</summary>
    public static IReadOnlyList<AudioBus> Buses => s_buses;

    /// <summary>Gets the root bus, which always exists and cannot be removed.</summary>
    public static AudioBus Master => s_byName[MasterBus];

    /// <summary>Gets every bus name, in <see cref="Buses"/> order — the option list for a bus picker.</summary>
    public static string[] BusNames => s_names ??= s_buses.Select(b => b.Name).ToArray();

    /// <summary>Gets whether any bus is currently soloed (in which case unrelated buses are silenced).</summary>
    public static bool AnySolo
    {
        get
        {
            foreach (AudioBus bus in s_buses)
            {
                if (bus.Solo)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Finds a bus by name (case-insensitive), or null when no such bus exists.</summary>
    public static AudioBus? Find(string? name)
        => string.IsNullOrEmpty(name) ? null : s_byName.GetValueOrDefault(name);

    /// <summary>Gets the parent of a bus, or null for the root (or when its parent has gone missing).</summary>
    public static AudioBus? ParentOf(AudioBus bus) => Find(bus.Parent);

    /// <summary>
    /// Gets the gain a sound on the named bus should play at: the product of that bus's fader and all of its
    /// ancestors', or 0 if any of them is muted or silenced by another bus's solo. An unknown bus name resolves
    /// to <see cref="MasterBus"/>.
    /// </summary>
    public static float GetGain(string? busName)
    {
        AudioBus bus = Find(busName) ?? Master;

        // Solo only needs testing on the routed bus itself: every ancestor of an audible bus is, by definition,
        // an ancestor of a soloed bus and therefore passes too.
        if (AnySolo && !PassesSolo(bus))
        {
            return 0.0f;
        }

        float gain = 1.0f;
        AudioBus? node = bus;
        for (int depth = 0; node is not null && depth < MaxDepth; depth++)
        {
            if (node.Mute)
            {
                return 0.0f;
            }

            gain *= node.Volume;
            node = ParentOf(node);
        }

        return gain;
    }

    /// <summary>
    /// Adds a bus under <paramref name="parent"/> and returns it. A name that is already taken gets a numeric
    /// suffix, and an unknown parent falls back to the root, so this always yields a usable bus.
    /// </summary>
    public static AudioBus AddBus(string name, string? parent = MasterBus)
    {
        string unique = UniqueName(string.IsNullOrWhiteSpace(name) ? "Bus" : name.Trim());
        AudioBus parentBus = Find(parent) ?? Master;
        var bus = new AudioBus(unique, parentBus.Name);
        s_buses.Add(bus);
        s_byName[unique] = bus;
        Invalidate();
        return bus;
    }

    /// <summary>
    /// Removes a bus, re-parenting its children to its own parent so nothing is orphaned. The root bus cannot be
    /// removed. Sounds still routed to the removed name fall back to the root.
    /// </summary>
    public static bool RemoveBus(string name)
    {
        AudioBus? bus = Find(name);
        if (bus is null || IsMaster(bus))
        {
            return false;
        }

        foreach (AudioBus child in s_buses)
        {
            if (string.Equals(child.Parent, bus.Name, StringComparison.OrdinalIgnoreCase))
            {
                child.Parent = bus.Parent ?? MasterBus;
            }
        }

        s_buses.Remove(bus);
        s_byName.Remove(bus.Name);
        Invalidate();
        return true;
    }

    /// <summary>
    /// Renames a bus, re-pointing its children at the new name. The root bus cannot be renamed. Audio sources
    /// referencing the old name are not rewritten — they fall back to the root until re-pointed, which the
    /// editor surfaces on the strip.
    /// </summary>
    public static bool RenameBus(string name, string newName)
    {
        AudioBus? bus = Find(name);
        if (bus is null || IsMaster(bus) || string.IsNullOrWhiteSpace(newName))
        {
            return false;
        }

        string trimmed = newName.Trim();
        if (string.Equals(bus.Name, trimmed, StringComparison.Ordinal))
        {
            return false;
        }

        // A case-only rename keeps the same slot; anything else has to dodge the names already in use.
        string unique = string.Equals(bus.Name, trimmed, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : UniqueName(trimmed);
        string old = bus.Name;

        s_byName.Remove(old);
        bus.Name = unique;
        s_byName[unique] = bus;

        foreach (AudioBus child in s_buses)
        {
            if (string.Equals(child.Parent, old, StringComparison.OrdinalIgnoreCase))
            {
                child.Parent = unique;
            }
        }

        Invalidate();
        return true;
    }

    /// <summary>
    /// Re-routes a bus under a new parent. Rejected for the root bus, an unknown parent, or a move that would
    /// make a bus its own ancestor.
    /// </summary>
    public static bool Reparent(string name, string parent)
    {
        AudioBus? bus = Find(name);
        AudioBus? parentBus = Find(parent);
        if (bus is null || parentBus is null || IsMaster(bus) ||
            ReferenceEquals(bus, parentBus) || IsDescendantOf(parentBus, bus))
        {
            return false;
        }

        bus.Parent = parentBus.Name;
        Invalidate();
        return true;
    }

    /// <summary>Replaces the whole bus layout with the default Master / Music / SFX / UI tree.</summary>
    public static void ResetToDefaults()
    {
        s_buses.Clear();
        s_byName.Clear();

        var master = new AudioBus(MasterBus, null);
        s_buses.Add(master);
        s_byName[MasterBus] = master;

        AddBus(MusicBus);
        AddBus(SfxBus);
        AddBus(UiBus);
        Invalidate();
    }

    /// <summary>
    /// Replaces the layout with an authored one (from a project or <c>game.manifest</c>). The root bus is always
    /// present, duplicate names are uniquified, and an unresolved or cyclic parent is re-pointed at the root, so
    /// a hand-edited or outdated layout still loads. A null or empty layout restores the defaults.
    /// </summary>
    public static void SetLayout(IEnumerable<AudioBusDefinition>? layout)
    {
        List<AudioBusDefinition>? defs = layout?
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Name))
            .ToList();

        if (defs is null || defs.Count == 0)
        {
            ResetToDefaults();
            return;
        }

        s_buses.Clear();
        s_byName.Clear();

        // The root is created first (adopting its authored level if the layout names it) so every other bus has
        // something valid to fall back to as a parent.
        AudioBusDefinition? masterDef = defs.FirstOrDefault(
            d => string.Equals(d.Name.Trim(), MasterBus, StringComparison.OrdinalIgnoreCase));
        var master = new AudioBus(MasterBus, null)
        {
            Volume = masterDef?.Volume ?? 1.0f,
            Mute = masterDef?.Mute ?? false,
        };
        s_buses.Add(master);
        s_byName[MasterBus] = master;

        foreach (AudioBusDefinition def in defs)
        {
            if (ReferenceEquals(def, masterDef))
            {
                continue;
            }

            AudioBus bus = AddBus(def.Name, MasterBus);
            bus.Volume = def.Volume;
            bus.Mute = def.Mute;
        }

        // Parents are applied in a second pass: a bus may be authored before the bus it hangs off.
        foreach (AudioBusDefinition def in defs)
        {
            if (ReferenceEquals(def, masterDef) || string.IsNullOrWhiteSpace(def.Parent))
            {
                continue;
            }

            AudioBus? bus = Find(def.Name.Trim());
            if (bus is null)
            {
                continue;
            }

            if (!Reparent(bus.Name, def.Parent!) && Find(def.Parent) is null)
            {
                Log.CoreWarn("Audio bus '{0}' names an unknown parent '{1}'; routing it to {2}.",
                    bus.Name, def.Parent, MasterBus);
            }
        }

        Invalidate();
    }

    /// <summary>Gets the current layout in serializable form, for saving into a project or a build manifest.</summary>
    public static List<AudioBusDefinition> GetLayout()
        => s_buses.Select(b => new AudioBusDefinition
        {
            Name = b.Name,
            Parent = b.Parent,
            Volume = b.Volume,
            Mute = b.Mute,
        }).ToList();

    /// <summary>Clears every solo, restoring the full mix.</summary>
    public static void ClearSolos()
    {
        foreach (AudioBus bus in s_buses)
        {
            bus.Solo = false;
        }
    }

    /// <summary>
    /// Records that a voice is sounding on a bus at the given gain, for the bus meters. Reported up the chain so
    /// a parent's meter reflects everything beneath it. Called by <see cref="AudioManager"/> once per frame per
    /// playing voice.
    /// </summary>
    internal static void ReportVoice(string? busName, float gain)
    {
        AudioBus? node = Find(busName) ?? Master;
        for (int depth = 0; node is not null && depth < MaxDepth; depth++)
        {
            node.PendingLevel = MathF.Max(node.PendingLevel, Math.Clamp(gain, 0.0f, 1.0f));
            node.PendingVoices++;
            node = ParentOf(node);
        }
    }

    /// <summary>Commits the frame's metering: meters rise instantly and fall away smoothly.</summary>
    internal static void Tick(float deltaTime)
    {
        float keep = MathF.Pow(MeterDecayPerSecond, MathF.Max(deltaTime, 0.0f));
        foreach (AudioBus bus in s_buses)
        {
            bus.Level = MathF.Max(bus.PendingLevel, bus.Level * keep);
            bus.ActiveVoices = bus.PendingVoices;
            bus.PendingLevel = 0.0f;
            bus.PendingVoices = 0;
        }
    }

    private static bool IsMaster(AudioBus bus) => ReferenceEquals(bus, Master);

    private static bool PassesSolo(AudioBus bus)
    {
        // Audible under solo if the bus is soloed, sits under a soloed bus, or is an ancestor carrying a soloed
        // bus's signal on toward the output.
        if (bus.Solo || IsUnderSolo(bus))
        {
            return true;
        }

        foreach (AudioBus other in s_buses)
        {
            if (other.Solo && IsDescendantOf(other, bus))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnderSolo(AudioBus bus)
    {
        AudioBus? node = ParentOf(bus);
        for (int depth = 0; node is not null && depth < MaxDepth; depth++)
        {
            if (node.Solo)
            {
                return true;
            }

            node = ParentOf(node);
        }

        return false;
    }

    private static bool IsDescendantOf(AudioBus bus, AudioBus ancestor)
    {
        AudioBus? node = ParentOf(bus);
        for (int depth = 0; node is not null && depth < MaxDepth; depth++)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = ParentOf(node);
        }

        return false;
    }

    private static string UniqueName(string name)
    {
        if (!s_byName.ContainsKey(name))
        {
            return name;
        }

        for (int i = 2; i < 1000; i++)
        {
            string candidate = $"{name} {i}";
            if (!s_byName.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return $"{name} {Guid.NewGuid():N}";
    }

    private static void Invalidate() => s_names = null;
}
