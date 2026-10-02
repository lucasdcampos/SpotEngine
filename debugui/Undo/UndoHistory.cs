using Spot.Framework;

namespace Spot.DebugUI.Undo;

/// <summary>
/// The editor's undo/redo history: one ordered list of actions plus a cursor marking how many are
/// currently applied. Everything before the cursor can be undone, everything at or after it can be
/// redone — which is what lets the History panel show the future as well as the past and jump to any
/// point in a single click.
/// </summary>
/// <remarks>
/// <para>
/// Actions are recorded at the exact boundary of a user edit rather than discovered by diffing state,
/// so a continuous drag is one entry and every entry can name what it did.
/// </para>
/// <para>
/// Per the engine's never-crash rule a faulty action cannot take the editor down: undo and redo wrap
/// each call, and an action that throws is dropped rather than left in place to throw again.
/// </para>
/// </remarks>
public sealed class UndoHistory
{
    /// <summary>The most entries kept; pushing past this drops the oldest.</summary>
    public const int MaxActions = 200;

    /// <summary>
    /// Total memory budget for recorded actions. Enforced alongside <see cref="MaxActions"/> because the
    /// catch-all snapshot action carries whole-scene JSON, and a few hundred of those would reintroduce
    /// the memory cost this system exists to remove.
    /// </summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    private readonly List<Entry> _entries = new();
    private int _cursor;
    private long _nextStamp = 1;
    private long _totalBytes;

    // Open Group(...) scopes. Actions pushed while grouping accumulate here and land as one
    // CompoundAction when the outermost scope closes. Nested groups flatten into the outer label.
    private readonly List<IUndoableAction> _groupBuffer = new();
    private int _groupDepth;
    private string _groupLabel = string.Empty;
    private SelectionSnapshot? _groupSelectionBefore;

    // Open Suspend() scopes. Non-zero means pushes are dropped on the floor.
    private int _suspendDepth;

    // True while Undo/Redo is applying an action, so a panel that reacts to the restored state by
    // editing it again cannot re-enter and corrupt the cursor.
    private bool _applying;

    // The selection as it stood before the next edit commits. Refreshed every idle frame so that an
    // action pushed after the user re-selects something records the right "before" selection.
    private SelectionSnapshot _selectionBaseline = SelectionSnapshot.Empty;

    private sealed record Entry(
        IUndoableAction Action,
        long Stamp,
        int Bytes,
        SelectionSnapshot Before,
        SelectionSnapshot After);

    /// <summary>Raised whenever the history or the cursor changes, so the UI can refresh.</summary>
    public event Action? Changed;

    /// <summary>
    /// Captures and restores the selection around each action. Left <see langword="null"/> (as in tests)
    /// the history simply does not touch the selection.
    /// </summary>
    public ISelectionStore? Selection { get; set; }

    /// <summary>
    /// Whether new actions are recorded. The editor clears this during play mode: panels still draw and
    /// still edit the live scene there, but those edits are discarded wholesale when play stops, so
    /// letting them into the history would fill it with entries that undo into a scene that no longer
    /// exists.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The recorded actions, oldest first. Indices below <see cref="Cursor"/> are applied.</summary>
    public IReadOnlyList<IUndoableAction> Actions => _entries.Select(e => e.Action).ToList();

    /// <summary>How many actions are currently applied; also the index of the next action to redo.</summary>
    public int Cursor => _cursor;

    /// <summary>Total recorded actions, applied and undone.</summary>
    public int Count => _entries.Count;

    /// <summary>Roughly how much memory the recorded actions hold.</summary>
    public long TotalBytes => _totalBytes;

    /// <summary>True while an undo or redo is being applied.</summary>
    public bool IsApplying => _applying;

    /// <summary>True while a <see cref="Suspend"/> scope is open.</summary>
    public bool IsSuspended => _suspendDepth > 0;

    /// <summary>Whether there is anything to undo.</summary>
    public bool CanUndo => _cursor > 0;

    /// <summary>Whether there is anything to redo.</summary>
    public bool CanRedo => _cursor < _entries.Count;

    /// <summary>The label of the action <see cref="Undo"/> would revert, or <see langword="null"/>.</summary>
    public string? UndoLabel => CanUndo ? _entries[_cursor - 1].Action.Label : null;

    /// <summary>The label of the action <see cref="Redo"/> would re-apply, or <see langword="null"/>.</summary>
    public string? RedoLabel => CanRedo ? _entries[_cursor].Action.Label : null;

    /// <summary>
    /// The label of the entry at an index, for the History panel. Returns <see langword="null"/> when the
    /// index is out of range.
    /// </summary>
    public string? LabelAt(int index) =>
        index >= 0 && index < _entries.Count ? _entries[index].Action.Label : null;

    /// <summary>
    /// Records an action the caller has already performed. Anything that had been undone is discarded —
    /// editing after an undo starts a new branch, as every editor does.
    /// </summary>
    /// <param name="action">The action to record.</param>
    /// <param name="selectionBefore">
    /// The selection as it was before the edit. When <see langword="null"/> the history's own idle
    /// baseline is used, which is correct for edits driven from the inspector or a gizmo.
    /// </param>
    public void Push(IUndoableAction action, SelectionSnapshot? selectionBefore = null)
    {
        // Applying an action must never record a new one; that would be a feedback loop.
        if (!Enabled || _applying || _suspendDepth > 0)
        {
            return;
        }

        if (_groupDepth > 0)
        {
            _groupSelectionBefore ??= selectionBefore ?? _selectionBaseline;
            _groupBuffer.Add(action);
            return;
        }

        if (CanRedo)
        {
            DropRange(_cursor, _entries.Count - _cursor);
        }

        SelectionSnapshot before = selectionBefore ?? _selectionBaseline;
        SelectionSnapshot after = Selection?.Capture() ?? SelectionSnapshot.Empty;
        int bytes = SizeOf(action);

        _entries.Add(new Entry(action, _nextStamp++, bytes, before, after));
        _totalBytes += bytes;
        _cursor = _entries.Count;
        _selectionBaseline = after;

        Trim();
        Changed?.Invoke();
    }

    /// <summary>
    /// Performs an action and records it. Used by structural operations (create, delete, reparent),
    /// where having the action itself do the work keeps the forward and reverse paths in one place.
    /// </summary>
    public void Execute(IUndoableAction action)
    {
        // Capture the pre-mutation selection before the action runs, since it is about to change.
        SelectionSnapshot before = Selection?.Capture() ?? SelectionSnapshot.Empty;

        if (!TryApply(action, redo: true))
        {
            return;
        }

        Push(action, before);
    }

    /// <summary>Reverts the most recent applied action. Returns false when there was nothing to undo.</summary>
    public bool Undo()
    {
        if (!CanUndo || _applying)
        {
            return false;
        }

        Entry entry = _entries[_cursor - 1];
        if (!TryApply(entry.Action, redo: false))
        {
            // The action is broken; drop it so the next Ctrl+Z reaches a working one.
            DropRange(_cursor - 1, 1);
            _cursor--;
            Changed?.Invoke();
            return false;
        }

        _cursor--;
        RestoreSelection(entry.Before);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Re-applies the next undone action. Returns false when there was nothing to redo.</summary>
    public bool Redo()
    {
        if (!CanRedo || _applying)
        {
            return false;
        }

        Entry entry = _entries[_cursor];
        if (!TryApply(entry.Action, redo: true))
        {
            DropRange(_cursor, 1);
            Changed?.Invoke();
            return false;
        }

        _cursor++;
        RestoreSelection(entry.After);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Moves to an arbitrary point in the history, undoing or redoing as many actions as it takes. This
    /// is what the History panel's click-to-jump does.
    /// </summary>
    /// <param name="cursor">How many actions should end up applied, clamped to the valid range.</param>
    public void JumpTo(int cursor)
    {
        cursor = Math.Clamp(cursor, 0, _entries.Count);
        while (_cursor > cursor && Undo())
        {
        }

        while (_cursor < cursor && Redo())
        {
        }
    }

    /// <summary>
    /// Opens a scope whose actions collapse into a single history entry, so deleting a multi-selection
    /// costs the user one <c>Ctrl</c>+<c>Z</c>. Dispose it (a <c>using</c> statement) to close the group.
    /// Nesting is allowed and flattens into the outermost label; a group with one action records that
    /// action directly, and an empty group records nothing.
    /// </summary>
    /// <param name="label">What the whole group did, e.g. "Delete 3 Entities".</param>
    public IDisposable Group(string label)
    {
        if (_groupDepth == 0)
        {
            _groupLabel = label;
            _groupBuffer.Clear();
            _groupSelectionBefore = null;
        }

        _groupDepth++;
        return new Scope(this, isGroup: true);
    }

    /// <summary>
    /// Opens a scope in which nothing is recorded. Required around inspector UI driven over scratch
    /// objects — the prefab editor builds a throwaway <see cref="Spot.Engine.Scenes.Scene"/>, and the material
    /// and model thumbnail helpers build their own — because the generic property editing those share
    /// with the real inspector would otherwise push actions targeting objects nobody owns.
    /// </summary>
    public IDisposable Suspend()
    {
        _suspendDepth++;
        return new Scope(this, isGroup: false);
    }

    /// <summary>Discards the entire history. Called when the project or open document set changes wholesale.</summary>
    public void Clear()
    {
        _entries.Clear();
        _groupBuffer.Clear();
        _groupDepth = 0;
        _groupSelectionBefore = null;
        _cursor = 0;
        _totalBytes = 0;
        _selectionBaseline = SelectionSnapshot.Empty;
        Changed?.Invoke();
    }

    /// <summary>
    /// Forgets every entry from the oldest one touching the given document onwards, called when a scene
    /// or UI document closes. Entries cannot simply be plucked from the middle: the remaining ones would
    /// no longer compose, and undoing across the gap would write into a document that is gone. Truncating
    /// is predictable and never touches dead objects.
    /// </summary>
    /// <param name="document">The document whose entries (and everything newer) should be dropped.</param>
    /// <returns>How many entries were discarded, so the caller can tell the user.</returns>
    public int DiscardDocument(object document)
    {
        int first = -1;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (ReferenceEquals(_entries[i].Action.Document, document))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            return 0;
        }

        int dropped = _entries.Count - first;
        DropRange(first, dropped);
        if (_cursor > first)
        {
            _cursor = first;
        }

        Changed?.Invoke();
        return dropped;
    }

    /// <summary>
    /// The stamp of the newest applied action belonging to a document, or 0 when none is applied.
    /// Comparing this with <see cref="IUndoDocument.CleanStamp"/> gives the unsaved-changes state without
    /// re-serializing anything, and correctly reports "clean" again when the user undoes all the way back
    /// to the saved state. Stamps are never reused, so a fresh edit after an undo cannot collide with the
    /// stamp the document was saved at.
    /// </summary>
    public long StampFor(object document)
    {
        for (int i = _cursor - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_entries[i].Action.Document, document))
            {
                return _entries[i].Stamp;
            }
        }

        return 0;
    }

    /// <summary>Whether a document has edits that are not in its last saved copy.</summary>
    public bool IsDirty(IUndoDocument document) => StampFor(document) != document.CleanStamp;

    /// <summary>Records a document's current state as its saved state.</summary>
    public void MarkSaved(IUndoDocument document) => document.CleanStamp = StampFor(document);

    /// <summary>
    /// Re-reads the current selection as the baseline for the next recorded action. Called once per frame
    /// while no edit is in flight, so an action records the selection the user actually had when they
    /// started the edit.
    /// </summary>
    public void RefreshSelectionBaseline()
    {
        if (_applying || Selection == null)
        {
            return;
        }

        _selectionBaseline = Selection.Capture();
    }

    private void RestoreSelection(SelectionSnapshot snapshot)
    {
        if (Selection == null)
        {
            return;
        }

        try
        {
            Selection.Apply(snapshot);
            _selectionBaseline = snapshot;
        }
        catch (Exception ex)
        {
            // A selection that cannot be restored is cosmetic; the state change itself already landed.
            Log.CoreWarn("Could not restore the selection for an undo step: {0}", ex.Message);
        }
    }

    private void EndGroup()
    {
        _groupDepth--;
        if (_groupDepth > 0 || _groupBuffer.Count == 0)
        {
            return;
        }

        IUndoableAction action = _groupBuffer.Count == 1
            ? _groupBuffer[0]
            : new CompoundAction(_groupLabel, _groupBuffer);
        SelectionSnapshot? before = _groupSelectionBefore;

        _groupBuffer.Clear();
        _groupSelectionBefore = null;
        Push(action, before);
    }

    // Enforces both caps, oldest first. Always leaves at least one entry so a single huge action is
    // still undoable.
    private void Trim()
    {
        if (_entries.Count > MaxActions)
        {
            DropOldest(_entries.Count - MaxActions);
        }

        while (_totalBytes > MaxBytes && _entries.Count > 1)
        {
            DropOldest(1);
        }
    }

    private void DropOldest(int count)
    {
        count = Math.Min(count, _entries.Count);
        DropRange(0, count);
        _cursor = Math.Max(0, _cursor - count);
    }

    private void DropRange(int index, int count)
    {
        for (int i = index; i < index + count; i++)
        {
            _totalBytes -= _entries[i].Bytes;
        }

        _entries.RemoveRange(index, count);
        if (_totalBytes < 0)
        {
            _totalBytes = 0;
        }
    }

    private static int SizeOf(IUndoableAction action)
    {
        try
        {
            return Math.Max(0, action.ApproxSizeBytes);
        }
        catch (Exception)
        {
            return 64;
        }
    }

    // Applies one direction of an action, keeping a throwing action from reaching the frame loop.
    private bool TryApply(IUndoableAction action, bool redo)
    {
        _applying = true;
        try
        {
            if (redo)
            {
                action.Redo();
            }
            else
            {
                action.Undo();
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.CoreError(
                "Undo action '{0}' failed and was dropped from the history: {1}", action.Label, ex.Message);
            return false;
        }
        finally
        {
            _applying = false;
        }
    }

    // One disposable serving both scope kinds, so a panel that throws mid-scope still unwinds it.
    private sealed class Scope(UndoHistory history, bool isGroup) : IDisposable
    {
        private bool _closed;

        public void Dispose()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            if (isGroup)
            {
                history.EndGroup();
            }
            else
            {
                history._suspendDepth--;
            }
        }
    }
}
