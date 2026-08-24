namespace Spot.Scenes;

/// <summary>
/// Holds the scripts attached to an entity. This is the single component type the script system
/// queries, so scripts of any concrete type are found through one pool. Attach scripts via
/// <see cref="Entity.AddScript{T}()"/> rather than manipulating this directly.
/// </summary>
[ComponentMenu("Script", Order = 100)]
public sealed class ScriptComponent : Component
{
    /// <summary>
    /// The scripts attached to the entity, in order. Each entry keeps the authored class name and, once
    /// the type resolves, the runtime instance whose public fields hold the serialized tunables — so the
    /// names and their instances can never drift out of alignment.
    /// </summary>
    public List<ScriptInstance> Items { get; } = new();

    /// <summary>The class names of all attached scripts, whether or not their type currently resolves.</summary>
    public IEnumerable<string> ClassNames => Items.Select(item => item.ClassName);

    /// <summary>The resolved runtime instances the script system runs; scripts with an unresolved type are skipped.</summary>
    internal IEnumerable<EntityBehaviour> Scripts => Items.Where(item => item.Instance is not null).Select(item => item.Instance!);
}

/// <summary>
/// One attached script: its stable <see cref="Guid"/> (from the script's <c>.cs.meta</c> sidecar), its
/// authored <see cref="ClassName"/>, and — when the type could be resolved — the runtime
/// <see cref="Instance"/>. The instance is <see langword="null"/> when the script's type isn't found (for
/// example a script that hasn't been compiled yet or was renamed), which the inspector surfaces to the user.
/// The guid is the primary reference so renaming the class does not break the scene; the class name is kept
/// as a human-readable, legacy fallback and may be empty when only a guid is known.
/// </summary>
public sealed class ScriptInstance
{
    /// <summary>Initializes a new entry for the given class name, optional resolved instance and stable guid.</summary>
    public ScriptInstance(string className, EntityBehaviour? instance = null, string guid = "")
    {
        ClassName = className;
        Instance = instance;
        Guid = guid;
    }

    /// <summary>Gets or sets the script's stable guid (from its <c>.cs.meta</c>), or empty when unknown.</summary>
    public string Guid { get; set; }

    /// <summary>Gets or sets the script's class name (the human-readable, legacy reference).</summary>
    public string ClassName { get; set; }

    /// <summary>Gets or sets the runtime instance, or <see langword="null"/> when the type is unresolved.</summary>
    public EntityBehaviour? Instance { get; set; }
}
