namespace Spot.Scenes;

/// <summary>
/// Marks an entity as an instance of a prefab asset. This is purely a link/marker: it records the source
/// prefab's <c>guid:</c> reference so the editor can tint the entity in the hierarchy and open the right
/// asset. Instances are independent, baked copies — this component does not synchronize an instance with
/// its prefab definition (there is no override tracking or propagation).
/// </summary>
/// <remarks>
/// It deliberately carries no <see cref="ComponentMenuAttribute"/>, so the reflection-based inspector never
/// draws it and it never appears in the "Add Component" menu. It does carry
/// <see cref="SceneComponentAttribute"/>, so the marking is written to and read from the scene like any other
/// component and survives a reload.
/// </remarks>
[SceneComponent("Prefab")]
public sealed class PrefabComponent : Component
{
    /// <summary>Gets or sets the <c>guid:</c> reference of the prefab asset this entity was instantiated from.</summary>
    public string? PrefabRef { get; set; }
}
