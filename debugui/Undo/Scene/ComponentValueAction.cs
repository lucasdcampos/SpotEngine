using Spot.Engine.Scenes;
using Spot.Framework;

namespace Spot.DebugUI.Undo;

/// <summary>
/// Restores one value on one component of one entity, resolving its target when it is applied rather
/// than holding a reference to it.
/// </summary>
/// <remarks>
/// Late resolution is not an optimization, it is a correctness requirement. Component objects do not
/// live as long as the history: undoing a delete re-instantiates the subtree with fresh component
/// instances, a script reload rebuilds every behaviour, and leaving play mode clears the scene and
/// deserializes it again. An action holding a captured component would quietly write into a dead
/// object. The stable <see cref="Entity.PersistentId"/> survives all of those (a full deserialize keeps
/// the stored id), so it is the only safe handle — and the <see cref="Spot.Engine.Scenes.Scene"/> instance is
/// safe to hold because the editor always re-hydrates a scene in place.
/// </remarks>
public sealed class ComponentValueAction : IUndoableAction
{
    private readonly Scene _scene;
    private readonly string _entityId;
    private readonly Type _componentType;
    private readonly MemberAccessor _member;
    private readonly object? _before;
    private readonly object? _after;
    private readonly Action<object>? _afterApply;

    /// <param name="label">What the edit did, e.g. "Set Intensity".</param>
    /// <param name="scene">The scene the entity lives in.</param>
    /// <param name="entityId">The target entity's stable id.</param>
    /// <param name="componentType">The component's runtime type.</param>
    /// <param name="member">The member to write.</param>
    /// <param name="before">The value before the edit.</param>
    /// <param name="after">The value after the edit.</param>
    /// <param name="document">The owning document, for the unsaved-changes marker.</param>
    /// <param name="afterApply">
    /// A side effect the edit performs today that must happen again on undo/redo, receiving the resolved
    /// component — re-validating a script, for instance.
    /// </param>
    public ComponentValueAction(
        string label, Scene scene, string entityId, Type componentType, MemberAccessor member,
        object? before, object? after, object? document = null, Action<object>? afterApply = null)
    {
        Label = label;
        _scene = scene;
        _entityId = entityId;
        _componentType = componentType;
        _member = member;
        _before = before;
        _after = after;
        Document = document;
        _afterApply = afterApply;
    }

    /// <inheritdoc />
    public string Label { get; }

    /// <inheritdoc />
    public object? Document { get; }

    /// <inheritdoc />
    public void Undo() => Apply(_before);

    /// <inheritdoc />
    public void Redo() => Apply(_after);

    private void Apply(object? value)
    {
        // A target that is gone is not an error worth failing the undo over: the user may have deleted
        // the entity and undone past that point. Log and move on, per the never-crash rule.
        if (_scene.EntityByPersistentId(_entityId) is not Entity entity)
        {
            Log.CoreWarn("Skipping '{0}': its entity no longer exists.", Label);
            return;
        }

        if (entity.GetComponent(_componentType) is not object component)
        {
            Log.CoreWarn("Skipping '{0}': the entity no longer has a {1}.", Label, _componentType.Name);
            return;
        }

        _member.Set(component, value);
        _afterApply?.Invoke(component);
    }
}
