namespace Spot.Scenes;

/// <summary>
/// A human-readable name attached to an entity. Every entity created by
/// <see cref="Scene.Instantiate"/> has one.
/// </summary>
public sealed class TagComponent : Component
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TagComponent"/> class.
    /// </summary>
    /// <param name="name">The entity name.</param>
    public TagComponent(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets or sets the entity name.
    /// </summary>
    public string Name { get; set; }
}
