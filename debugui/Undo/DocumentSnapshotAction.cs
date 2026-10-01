namespace Spot.DebugUI.Undo;

/// <summary>
/// Restores a whole document from a serialized snapshot taken before and after a change. The coarse
/// instrument of the system, used in two situations where it is the right one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Small documents.</b> An Animator Controller has roughly thirty separate mutation sites (states,
/// transitions, parameters, conditions, clip assignments, node positions); a material, a mixer layout
/// and a <c>.sptui</c> widget tree are similar in spirit. Snapshotting these is a few hundred bytes,
/// hard to get wrong, and replaces thirty bespoke actions with one.
/// </para>
/// <para>
/// <b>The catch-all.</b> The editor also pushes one of these whenever its periodic scene check finds a
/// change that no action accounts for. That is what makes <c>Ctrl</c>+<c>Z</c> complete from the first
/// day rather than only once every mutation site has been migrated: an un-migrated site produces a
/// coarse "Scene Change" entry, never nothing at all. Snapshots are large, which is why
/// <see cref="UndoHistory"/> enforces a byte budget as well as an entry count.
/// </para>
/// </remarks>
public sealed class DocumentSnapshotAction : IUndoableAction
{
    private readonly string _before;
    private readonly string _after;
    private readonly Action<string> _restore;

    /// <param name="label">What the change did, e.g. "Add Animator State".</param>
    /// <param name="document">The owning document, for the unsaved-changes marker.</param>
    /// <param name="before">The document serialized before the change.</param>
    /// <param name="after">The document serialized after the change.</param>
    /// <param name="restore">
    /// Re-hydrates the document from a snapshot. Must restore <em>into the existing instance</em> rather
    /// than replacing it — panels, open tabs and the selection all hold references to the live object.
    /// </param>
    public DocumentSnapshotAction(
        string label, object? document, string before, string after, Action<string> restore)
    {
        Label = label;
        Document = document;
        _before = before;
        _after = after;
        _restore = restore;
    }

    /// <inheritdoc />
    public string Label { get; }

    /// <inheritdoc />
    public object? Document { get; }

    /// <inheritdoc />
    public int ApproxSizeBytes => (_before.Length + _after.Length) * sizeof(char);

    /// <inheritdoc />
    public void Undo() => _restore(_before);

    /// <inheritdoc />
    public void Redo() => _restore(_after);
}
