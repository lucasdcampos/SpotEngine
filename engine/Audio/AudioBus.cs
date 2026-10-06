namespace Spot.Engine.Audio;

/// <summary>
/// One volume group in the <see cref="AudioMixer"/> tree — a named fader every sound routed to it passes
/// through. Buses form a hierarchy ("Music" and "SFX" under "Master"), so lowering a parent lowers everything
/// beneath it. A bus holds only mix state (level, mute, solo); it owns no voices, and sounds reference it by
/// name so a scene never carries a hard reference to a mixer object.
/// </summary>
public sealed class AudioBus
{
    private float _volume = 1.0f;

    internal AudioBus(string name, string? parent)
    {
        Name = name;
        Parent = parent;
    }

    /// <summary>Gets the bus name, unique (case-insensitively) within the mixer.</summary>
    public string Name { get; internal set; }

    /// <summary>Gets the name of this bus's parent bus, or null for the root <see cref="AudioMixer.MasterBus"/>.</summary>
    public string? Parent { get; internal set; }

    /// <summary>Gets or sets this bus's own fader, clamped to [0, 1]. The audible level also includes its ancestors.</summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>Gets or sets whether this bus (and everything under it) is silenced.</summary>
    public bool Mute { get; set; }

    /// <summary>
    /// Gets or sets whether this bus is soloed. While any bus in the mixer is soloed, only the soloed buses,
    /// their descendants, and their ancestors are audible — the usual way to audition one group in isolation.
    /// </summary>
    public bool Solo { get; set; }

    /// <summary>
    /// Gets the meter level of this bus: how loudly it is currently contributing, in [0, 1], including the
    /// buses beneath it. It is derived from the effective gain of the voices playing through the bus (not from
    /// decoded samples, which the backends do not expose), so it reads as activity rather than a true peak
    /// meter. It decays smoothly so the editor can draw it as a level bar.
    /// </summary>
    public float Level { get; internal set; }

    /// <summary>Gets the number of voices currently playing through this bus, including the buses beneath it.</summary>
    public int ActiveVoices { get; internal set; }

    // Accumulators for the frame in progress; committed to Level/ActiveVoices by AudioMixer.Tick.
    internal float PendingLevel;
    internal int PendingVoices;
}

/// <summary>
/// The serializable form of one <see cref="AudioBus"/>: the authored bus layout stored in a project and
/// shipped in <c>game.manifest</c>, so a game boots with the same groups the editor was mixed with. Runtime
/// state (solo, metering) is deliberately absent — it belongs to the session, not to the project.
/// </summary>
public sealed class AudioBusDefinition
{
    /// <summary>Gets or sets the bus name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the parent bus name, or null/empty for the root bus.</summary>
    public string? Parent { get; set; }

    /// <summary>Gets or sets the bus fader, in [0, 1].</summary>
    public float Volume { get; set; } = 1.0f;

    /// <summary>Gets or sets whether the bus starts muted.</summary>
    public bool Mute { get; set; }
}
