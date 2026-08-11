using System;

namespace Spot.Scenes;

/// <summary>
/// Marks a <see cref="Component"/> type as serializable to and from a <c>.sptscene</c> file and gives
/// it a stable on-disk key. The key is deliberately decoupled from the class name so renaming a
/// component never silently changes the scene format (which would break existing scenes).
/// </summary>
/// <remarks>
/// The scene serializer discovers every component carrying this attribute by reflection and reads and
/// writes its public properties automatically (see <see cref="ComponentSerialization"/>), so adding a
/// serializable component means adding the attribute — no per-component serialization code. Components
/// without it are not written (for example <see cref="RelationshipComponent"/>, reconstructed from the
/// entity hierarchy; <see cref="LabelComponent"/> and <see cref="ScriptComponent"/>, handled specially by
/// the serializer).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SceneComponentAttribute : Attribute
{
    /// <summary>Initializes the attribute with the component's stable on-disk key.</summary>
    /// <param name="key">The property name this component is written under in a scene file.</param>
    public SceneComponentAttribute(string key)
    {
        Key = key;
    }

    /// <summary>Gets the stable on-disk key this component is serialized under.</summary>
    public string Key { get; }
}
