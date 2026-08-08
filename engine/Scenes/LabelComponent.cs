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
    /// Gets or sets the entity name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Whether this entity survives a scene switch instead of being destroyed with its scene (the
    /// engine's equivalent of Unity's <c>DontDestroyOnLoad</c>). Set through
    /// <see cref="Entity.DontDestroyOnLoad"/>; a runtime concept, not serialized. Only meaningful on a
    /// root entity — the whole subtree migrates with it.
    /// </summary>
    internal bool Persistent { get; set; }
}
