namespace Spot.DebugUI.Undo;

/// <summary>
/// Several actions treated as one history entry, so that deleting a multi-selection or changing an
/// asset slot (which writes both the loaded object and its stored reference) costs the user a single
/// <c>Ctrl</c>+<c>Z</c>. Produced by <see cref="UndoHistory.Group"/>.
/// </summary>
public sealed class CompoundAction : IUndoableAction
{
    private readonly IUndoableAction[] _actions;

    /// <param name="label">What the whole group did, e.g. "Delete 3 Entities".</param>
    /// <param name="actions">The constituent actions, in the order they were performed.</param>
    public CompoundAction(string label, IEnumerable<IUndoableAction> actions)
    {
        Label = label;
        _actions = actions.ToArray();
        // The group belongs to a document only when every part of it does, so a mixed group (a scene
        // edit that also touched a global) never marks the wrong document dirty.
        object? document = _actions.Length > 0 ? _actions[0].Document : null;
        Document = document != null && _actions.All(a => ReferenceEquals(a.Document, document)) ? document : null;
    }

    /// <inheritdoc />
    public string Label { get; }

    /// <inheritdoc />
    public object? Document { get; }

    /// <summary>The actions in this group, for inspection by the History panel and tests.</summary>
    public IReadOnlyList<IUndoableAction> Actions => _actions;

    /// <inheritdoc />
    public void Undo()
    {
        // Reverse order: the last change made is the first one taken back.
        for (int i = _actions.Length - 1; i >= 0; i--)
        {
            _actions[i].Undo();
        }
    }

    /// <inheritdoc />
    public void Redo()
    {
        foreach (IUndoableAction action in _actions)
        {
            action.Redo();
        }
    }
}
