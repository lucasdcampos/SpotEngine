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
    /// Gets the class names of the scripts attached to the entity.
    /// </summary>
    [HideInInspector]
    public List<string> ClassNames { get; set; } = new();

    /// <summary>
    /// Gets the scripts attached to the entity (instantiated at runtime).
    /// </summary>
    internal List<EntityBehaviour> Scripts { get; } = new();
}
