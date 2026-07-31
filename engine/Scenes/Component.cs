namespace Spot.Scenes;

/// <summary>
/// Base class for all components, providing an Enabled state.
/// </summary>
public abstract class Component
{
    /// <summary>
    /// Gets or sets a value indicating whether this component is active.
    /// Inactive components are ignored by systems (e.g. not rendered or updated).
    /// </summary>
    public bool Enabled { get; set; } = true;
}
