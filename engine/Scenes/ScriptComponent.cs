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
/// One attached script: its authored class name and, when the type could be resolved, the runtime
/// instance. The instance is <see langword="null"/> when the script's type isn't found (for example a
/// script that hasn't been compiled yet or was renamed), which the inspector surfaces to the user.
/// </summary>
public sealed class ScriptInstance
{
    /// <summary>Initializes a new entry for the given class name and optional resolved instance.</summary>
    public ScriptInstance(string className, EntityBehaviour? instance = null)
    {
        ClassName = className;
        Instance = instance;
    }

    /// <summary>Gets or sets the script's class name (the serialized reference).</summary>
    public string ClassName { get; set; }

    /// <summary>Gets or sets the runtime instance, or <see langword="null"/> when the type is unresolved.</summary>
    public EntityBehaviour? Instance { get; set; }
}
