namespace Spot.Scenes;

/// <summary>
/// A human-readable name attached to an entity. Every entity created by
/// <see cref="Scene.Instantiate"/> has one.
/// </summary>
public sealed class LabelComponent : Component
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LabelComponent"/> class.
    /// </summary>
    /// <param name="name">The entity name.</param>
    public LabelComponent(string name)
    {
        Name = name;
    }

    /// <summary>
    /// The scene that owns this component, set when the entity is attached to a scene (and rebound on scene
    /// migration). Lets an enabled-state change invalidate the scene's memoized hierarchy-active results,
    /// which <see cref="Entity.IsActiveInHierarchy"/> reads many times per frame.
    /// </summary>
    internal Scene? OwnerScene { get; set; }

    /// <summary>
    /// Whether the entity is active. Overrides <see cref="Component.Enabled"/> to notify the owning scene
    /// when it changes, because this flag (walked up the parent chain) is exactly what
    /// <see cref="Entity.IsActiveInHierarchy"/> returns — and the scene caches that result.
    /// </summary>
    public override bool Enabled
    {
        get => base.Enabled;
        set
        {
            if (base.Enabled == value)
            {
                return;
            }

            base.Enabled = value;
            OwnerScene?.InvalidateHierarchyActive();
        }
    }

    /// <summary>
    /// Gets or sets the entity name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the entity's tag: a free-form category string used to find entities
    /// (<see cref="Scene.FindByTag"/>) or classify them (<see cref="Entity.CompareTag"/>). Empty by
    /// default. Unlike <see cref="Name"/>, tags are not required to be unique.
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// A stable, per-entity identifier used to reference this entity from serialized data (for example an
    /// <see cref="Entity"/>-typed script field). Assigned on save and preserved on load so a reference
    /// survives renames and reordering; empty until the entity is first serialized. Not a
    /// <see cref="SceneComponentAttribute"/> field — the scene serializer writes it in the structural
    /// <c>Tag</c> block, and prefab instances are given fresh ids to avoid collisions.
    /// </summary>
    internal string EntityGuid { get; set; } = string.Empty;

    /// <summary>
    /// Whether this entity survives a scene switch instead of being destroyed with its scene (the
    /// engine's equivalent of Unity's <c>DontDestroyOnLoad</c>). Set through
    /// <see cref="Entity.DontDestroyOnLoad"/>; a runtime concept, not serialized. Only meaningful on a
    /// root entity — the whole subtree migrates with it.
    /// </summary>
    internal bool Persistent { get; set; }
}
