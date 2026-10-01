namespace Spot.DebugUI.Undo;

/// <summary>
/// Restores a single value by handing it back to a setter, carrying the before and after values with
/// it.
/// </summary>
/// <remarks>
/// <b>Only for targets that outlive the history.</b> The setter is a closure, so it captures whatever
/// object it writes to — which makes this the right tool for statics, for the path-cached singletons
/// (<see cref="Spot.Assets.Material"/>, <c>AnimatorController</c>), for <c>ProjectConfig</c>, and for
/// an audio bus resolved by name. It is the <em>wrong</em> tool for scene data: component instances
/// are replaced wholesale by a delete-undo, a script reload, or leaving play mode, and a captured one
/// would silently become a write into a dead object. Use <see cref="ComponentValueAction"/> there,
/// which resolves its target when applied.
/// </remarks>
/// <typeparam name="T">The value's type.</typeparam>
public sealed class ValueAction<T> : IUndoableAction
{
    private readonly Action<T> _apply;
    private readonly Action? _afterApply;
    private readonly T _before;
    private readonly T _after;

    /// <param name="label">What the edit did, e.g. "Set Intensity".</param>
    /// <param name="apply">Writes the value. Called with <paramref name="before"/> on undo and
    /// <paramref name="after"/> on redo.</param>
    /// <param name="before">The value as it was before the edit.</param>
    /// <param name="after">The value the edit produced.</param>
    /// <param name="document">The owning document, for the unsaved-changes marker.</param>
    /// <param name="afterApply">A side effect the edit already performs today and that must happen
    /// again on undo/redo — writing a material back to disk, re-validating a script.</param>
    public ValueAction(string label, Action<T> apply, T before, T after,
        object? document = null, Action? afterApply = null)
    {
        Label = label;
        _apply = apply;
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
    public void Undo()
    {
        _apply(_before);
        _afterApply?.Invoke();
    }

    /// <inheritdoc />
    public void Redo()
    {
        _apply(_after);
        _afterApply?.Invoke();
    }
}
