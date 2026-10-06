using ImGuiNET;
using Spot.Engine;

namespace Spot.DebugUI.Undo;

/// <summary>
/// Turns the editor's immediate-mode field editing into undo entries, collapsing a continuous edit (a
/// slider drag, a color-picker session, a burst of arrow-key nudges) into exactly one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not ImGui's item boundaries.</b> <c>IsItemActivated</c>/<c>IsItemDeactivatedAfterEdit</c>
/// look like the obvious tool but cannot express the editor's compound controls:
/// <c>ImGui.ColorEdit4</c> rewrites the last-item id at the end of its own call, so while a sub-slider
/// is being dragged the deactivation test compares two different ids and never fires; the label
/// helpers in <see cref="UI.EditorGui"/> open and close an ImGui column layout around the widget, so
/// a caller reading item state afterwards sees whichever sub-widget happened to be last (which
/// changes with the control's own state); and an asset slot commits through three unrelated items — a
/// drag-drop target, a <c>Selectable</c> in its picker popup, and a clear button.
/// </para>
/// <para>
/// <b>What is used instead.</b> The <c>bool changed</c> every helper already returns identifies the
/// edit precisely, and the pre-widget value is the value before it. ImGui permits at most one active
/// item at a time, so a single pending slot suffices — no per-control ids, and not one line of change
/// in the widget helpers. The pending edit stays open while ImGui reports interaction and is committed
/// once it goes idle. Note the difference from the polled history this replaces: the idle test only
/// decides <em>when to close</em> an edit the <c>changed</c> flag already identified, it is never used
/// to <em>discover</em> that something changed.
/// </para>
/// </remarks>
public static class UndoTracker
{
    /// <summary>
    /// The history edits are recorded into. Defaults to <see cref="EditorHistory.Current"/>; tests
    /// assign their own.
    /// </summary>
    public static UndoHistory History { get; set; } = EditorHistory.Current;

    /// <summary>
    /// Whether the user is still mid-interaction. Overridable so the state machine can be tested
    /// without an ImGui context.
    /// </summary>
    public static Func<bool> IsInteracting { get; set; } = DefaultIsInteracting;

    private static IPendingEdit? _pending;

    /// <summary>
    /// Records an edit in progress. Call immediately after the widget helper, passing the value read
    /// <em>before</em> it ran.
    /// </summary>
    /// <typeparam name="T">The edited value's type.</typeparam>
    /// <param name="key">Identity of the edited data; see <see cref="UndoKey"/>.</param>
    /// <param name="label">What the edit does, e.g. "Set Intensity".</param>
    /// <param name="before">The value as it was before this frame's widget call.</param>
    /// <param name="read">Reads the value as it stands now.</param>
    /// <param name="make">Builds the action from the before and after values.</param>
    /// <param name="changed">Whether the widget reported a change this frame.</param>
    /// <param name="idleMs">
    /// How long to hold the edit open after the last change when nothing is interacting. Zero suits
    /// mouse-driven edits (ImGui reports an active item throughout). Keyboard-driven repeats — arrow-key
    /// nudges, reorder shortcuts — need a window so a burst collapses into one entry.
    /// </param>
    public static void Track<T>(
        UndoKey key, string label, T before, Func<T> read, Func<T, T, IUndoableAction> make,
        bool changed, int idleMs = 0)
    {
        // Focus moved to different data without the previous edit being committed (Tab, or a click
        // straight onto another field): close the old one first so neither is lost.
        if (_pending != null && !_pending.Key.Equals(key))
        {
            Flush();
        }

        if (!changed)
        {
            return;
        }

        _pending ??= new PendingEdit<T>(key, label, before, read, make, idleMs);
        _pending.LastChangeTick = Environment.TickCount64;
    }

    /// <summary>
    /// Commits a pending edit once the interaction behind it has settled. Called once per frame, at the
    /// very end of the editor's ImGui pass.
    /// </summary>
    public static void EndFrame()
    {
        if (_pending == null)
        {
            History.RefreshSelectionBaseline();
            return;
        }

        bool interacting;
        try
        {
            interacting = IsInteracting();
        }
        catch (Exception)
        {
            // Without a usable interaction signal, commit rather than hold the edit open forever.
            interacting = false;
        }

        if (interacting)
        {
            return;
        }

        if (_pending.IdleMs > 0 && Environment.TickCount64 - _pending.LastChangeTick < _pending.IdleMs)
        {
            return;
        }

        Flush();
    }

    /// <summary>
    /// Commits any pending edit immediately. Called before handling an undo/redo shortcut, before a
    /// save, and by <see cref="UndoHistory.Push"/> itself, so a half-finished edit always lands in the
    /// history ahead of whatever comes next rather than after it.
    /// </summary>
    public static void Flush()
    {
        IPendingEdit? pending = _pending;
        if (pending == null)
        {
            return;
        }

        // Cleared up front so a throwing build cannot leave the slot occupied, and so the Push below
        // (which calls back into Flush) sees nothing pending.
        _pending = null;

        try
        {
            if (pending.TryBuild(out IUndoableAction? action) && action != null)
            {
                History.Push(action, pending.SelectionBefore);
            }
        }
        catch (Exception ex)
        {
            Log.CoreWarn(
                "Could not record '{0}' ({1}) in the undo history: {2}",
                pending.Label, pending.Key, ex.Message);
        }
    }

    /// <summary>
    /// Drops any pending edit without recording it. Used when the state behind it is about to be
    /// discarded anyway — entering play mode, or closing the document being edited.
    /// </summary>
    public static void Abandon() => _pending = null;

    /// <summary>Whether an edit is currently open. Exposed for tests and diagnostics.</summary>
    public static bool HasPending => _pending != null;

    private static bool DefaultIsInteracting() =>
        ImGui.IsAnyItemActive() || ImGui.GetIO().MouseDown[0];

    private interface IPendingEdit
    {
        UndoKey Key { get; }

        string Label { get; }

        int IdleMs { get; }

        long LastChangeTick { get; set; }

        SelectionSnapshot? SelectionBefore { get; }

        bool TryBuild(out IUndoableAction? action);
    }

    private sealed class PendingEdit<T> : IPendingEdit
    {
        private readonly T _before;
        private readonly Func<T> _read;
        private readonly Func<T, T, IUndoableAction> _make;

        public PendingEdit(
            UndoKey key, string label, T before, Func<T> read, Func<T, T, IUndoableAction> make, int idleMs)
        {
            Key = key;
            Label = label;
            _before = before;
            _read = read;
            _make = make;
            IdleMs = idleMs;
            LastChangeTick = Environment.TickCount64;

            // The selection as it was when the edit began, so undoing it restores that selection rather
            // than whatever the user happened to click next.
            SelectionBefore = UndoTracker.History.Selection?.Capture();
        }

        public UndoKey Key { get; }

        public string Label { get; }

        public int IdleMs { get; }

        public long LastChangeTick { get; set; }

        public SelectionSnapshot? SelectionBefore { get; }

        public bool TryBuild(out IUndoableAction? action)
        {
            action = null;
            T after = _read();

            // Nothing to record when the value came back to where it started — which is also how an
            // Escape-cancelled text edit resolves itself, with no special case.
            if (EqualityComparer<T>.Default.Equals(_before, after))
            {
                return false;
            }

            action = _make(_before, after);
            return true;
        }
    }
}
