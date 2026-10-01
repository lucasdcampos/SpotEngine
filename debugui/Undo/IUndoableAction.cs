namespace Spot.DebugUI.Undo;

/// <summary>
/// One reversible editor operation. Actions are recorded at the exact boundary of a user edit (a
/// released slider, a committed text field, a finished gizmo drag) rather than discovered by diffing
/// state, so a continuous drag becomes a single entry and every entry can name what it did.
/// </summary>
/// <remarks>
/// <see cref="Undo"/> and <see cref="Redo"/> must be idempotent in the sense that applying them in
/// alternation any number of times leaves the same two states — they carry the before and after
/// values with them rather than re-deriving anything from the live scene.
/// </remarks>
public interface IUndoableAction
{
    /// <summary>
    /// Human-readable description of the operation, shown in the Edit menu ("Undo Move Cube") and the
    /// History panel. Written in the imperative, without the "Undo" prefix.
    /// </summary>
    string Label { get; }

    /// <summary>
    /// The document this action belongs to (an open scene, a UI document, an asset path), or
    /// <see langword="null"/> when it changes global state that is not part of any document. Used to
    /// drive each document's unsaved-changes marker; see <see cref="IUndoDocument"/>.
    /// </summary>
    object? Document { get; }

    /// <summary>
    /// Roughly how much memory this action holds onto, in bytes. The history enforces a total budget
    /// alongside its entry count, because the catch-all snapshot action carries whole-scene JSON and a
    /// few hundred of those would reintroduce the memory problem this system exists to remove.
    /// Actions that hold only a couple of values can leave this at its default.
    /// </summary>
    int ApproxSizeBytes => 64;

    /// <summary>Reverts the operation, restoring the state captured before it ran.</summary>
    void Undo();

    /// <summary>Re-applies the operation.</summary>
    void Redo();
}

/// <summary>
/// A document whose unsaved-changes state is derived from the undo history instead of by
/// re-serializing it on a timer. Saving records <see cref="CleanStamp"/>; the document is dirty
/// whenever <see cref="UndoHistory.StampFor"/> disagrees with it.
/// </summary>
public interface IUndoDocument
{
    /// <summary>The history stamp this document was last saved at. Zero means "never edited".</summary>
    long CleanStamp { get; set; }
}
