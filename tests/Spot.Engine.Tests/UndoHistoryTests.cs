using Spot.DebugUI.Undo;

namespace Spot.Engine.Tests;

public class UndoHistoryTests
{
    // A minimal action that writes a value into a box, so the tests can assert on applied state rather
    // than on the history's bookkeeping alone.
    private sealed class SetAction(string label, int[] box, int before, int after, object? document = null)
        : IUndoableAction
    {
        public string Label => label;

        public object? Document => document;

        public int ApproxSizeBytes { get; init; } = 64;

        public void Undo() => box[0] = before;

        public void Redo() => box[0] = after;
    }

    private sealed class ThrowingAction : IUndoableAction
    {
        public string Label => "Boom";

        public object? Document => null;

        public void Undo() => throw new InvalidOperationException("nope");

        public void Redo() => throw new InvalidOperationException("nope");
    }

    private sealed class Doc : IUndoDocument
    {
        public long CleanStamp { get; set; }
    }

    [Fact]
    public void UndoAndRedoWalkTheCursorAndApplyState()
    {
        var history = new UndoHistory();
        int[] box = [0];

        box[0] = 1;
        history.Push(new SetAction("A", box, 0, 1));
        box[0] = 2;
        history.Push(new SetAction("B", box, 1, 2));

        Assert.Equal(2, history.Cursor);
        Assert.Equal("B", history.UndoLabel);
        Assert.Null(history.RedoLabel);

        Assert.True(history.Undo());
        Assert.Equal(1, box[0]);
        Assert.Equal("A", history.UndoLabel);
        Assert.Equal("B", history.RedoLabel);

        Assert.True(history.Undo());
        Assert.Equal(0, box[0]);
        Assert.False(history.CanUndo);
        Assert.False(history.Undo());

        Assert.True(history.Redo());
        Assert.True(history.Redo());
        Assert.Equal(2, box[0]);
        Assert.False(history.Redo());
    }

    [Fact]
    public void PushingAfterAnUndoDiscardsTheRedoBranch()
    {
        var history = new UndoHistory();
        int[] box = [0];

        history.Push(new SetAction("A", box, 0, 1));
        history.Push(new SetAction("B", box, 1, 2));
        history.Undo();

        Assert.True(history.CanRedo);

        history.Push(new SetAction("C", box, 1, 9));

        Assert.False(history.CanRedo);
        Assert.Equal(2, history.Count);
        Assert.Equal("C", history.UndoLabel);
    }

    [Fact]
    public void ExecuteAppliesTheActionAndRecordsIt()
    {
        var history = new UndoHistory();
        int[] box = [0];

        history.Execute(new SetAction("A", box, 0, 5));

        Assert.Equal(5, box[0]);
        Assert.Equal(1, history.Count);
        Assert.Equal(1, history.Cursor);
    }

    [Fact]
    public void JumpToMovesInBothDirections()
    {
        var history = new UndoHistory();
        int[] box = [0];

        for (int i = 1; i <= 5; i++)
        {
            history.Push(new SetAction($"A{i}", box, i - 1, i));
        }

        history.JumpTo(2);
        Assert.Equal(2, history.Cursor);
        Assert.Equal(2, box[0]);

        history.JumpTo(5);
        Assert.Equal(5, history.Cursor);
        Assert.Equal(5, box[0]);

        // Out-of-range targets clamp rather than throw.
        history.JumpTo(-3);
        Assert.Equal(0, history.Cursor);
        history.JumpTo(99);
        Assert.Equal(5, history.Cursor);
    }

    [Fact]
    public void GroupCollapsesSeveralActionsIntoOneEntryAndUndoesInReverse()
    {
        var history = new UndoHistory();
        var order = new List<string>();
        int[] box = [0];

        using (history.Group("Delete 3 Entities"))
        {
            history.Push(new SetAction("one", box, 0, 1));
            history.Push(new SetAction("two", box, 1, 2));
            history.Push(new SetAction("three", box, 2, 3));
        }

        Assert.Equal(1, history.Count);
        Assert.Equal("Delete 3 Entities", history.UndoLabel);

        // Reverse order on undo is what makes a compound revert correctly.
        var compound = new CompoundAction("g",
        [
            new DelegateProbe(() => order.Add("undo-1"), () => order.Add("redo-1")),
            new DelegateProbe(() => order.Add("undo-2"), () => order.Add("redo-2")),
        ]);
        compound.Undo();
        Assert.Equal(["undo-2", "undo-1"], order);
    }

    private sealed class DelegateProbe(Action undo, Action redo) : IUndoableAction
    {
        public string Label => "probe";

        public object? Document => null;

        public void Undo() => undo();

        public void Redo() => redo();
    }

    [Fact]
    public void ASingleActionGroupRecordsThatActionDirectlyAndAnEmptyGroupRecordsNothing()
    {
        var history = new UndoHistory();
        int[] box = [0];

        using (history.Group("Outer"))
        {
            history.Push(new SetAction("only", box, 0, 1));
        }

        Assert.Equal(1, history.Count);
        Assert.Equal("only", history.UndoLabel);

        using (history.Group("Nothing"))
        {
        }

        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void NestedGroupsFlattenIntoTheOutermostEntry()
    {
        var history = new UndoHistory();
        int[] box = [0];

        using (history.Group("Outer"))
        {
            history.Push(new SetAction("a", box, 0, 1));
            using (history.Group("Inner"))
            {
                history.Push(new SetAction("b", box, 1, 2));
            }

            Assert.Equal(0, history.Count);
        }

        Assert.Equal(1, history.Count);
        Assert.Equal("Outer", history.UndoLabel);
    }

    [Fact]
    public void SuspendDropsPushesAndDisabledHistoryRecordsNothing()
    {
        var history = new UndoHistory();
        int[] box = [0];

        using (history.Suspend())
        {
            history.Push(new SetAction("ignored", box, 0, 1));
            Assert.True(history.IsSuspended);
        }

        Assert.Equal(0, history.Count);
        Assert.False(history.IsSuspended);

        history.Enabled = false;
        history.Push(new SetAction("also ignored", box, 0, 1));
        Assert.Equal(0, history.Count);

        history.Enabled = true;
        history.Push(new SetAction("kept", box, 0, 1));
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void TheEntryCountIsCapped()
    {
        var history = new UndoHistory();
        int[] box = [0];

        for (int i = 0; i < UndoHistory.MaxActions + 50; i++)
        {
            history.Push(new SetAction($"A{i}", box, 0, 1));
        }

        Assert.Equal(UndoHistory.MaxActions, history.Count);
        Assert.Equal(UndoHistory.MaxActions, history.Cursor);
        // The newest action survives; the oldest were dropped.
        Assert.Equal($"A{UndoHistory.MaxActions + 49}", history.UndoLabel);
    }

    [Fact]
    public void TheByteBudgetEvictsOldSnapshotsButAlwaysKeepsOne()
    {
        var history = new UndoHistory();
        int[] box = [0];
        int huge = (int)(UndoHistory.MaxBytes / 3) + 1;

        for (int i = 0; i < 6; i++)
        {
            history.Push(new SetAction($"big{i}", box, 0, 1) { ApproxSizeBytes = huge });
        }

        Assert.True(history.Count >= 1);
        Assert.True(history.TotalBytes <= UndoHistory.MaxBytes || history.Count == 1);
        Assert.Equal("big5", history.UndoLabel);
    }

    [Fact]
    public void AThrowingActionIsDroppedInsteadOfCrashing()
    {
        var history = new UndoHistory();

        history.Push(new ThrowingAction());
        Assert.Equal(1, history.Count);

        // The undo reports failure and removes the entry rather than letting the exception escape.
        Assert.False(history.Undo());
        Assert.Equal(0, history.Count);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void DirtyTrackingSurvivesTheUndoRedoRelapseThatBreaksACounter()
    {
        var history = new UndoHistory();
        var doc = new Doc();
        int[] box = [0];

        history.Push(new SetAction("A", box, 0, 1, doc));
        history.Push(new SetAction("B", box, 1, 2, doc));
        history.MarkSaved(doc);
        Assert.False(history.IsDirty(doc));

        history.Undo();
        Assert.True(history.IsDirty(doc));

        history.Redo();
        Assert.False(history.IsDirty(doc));

        // The case a bump/unbump counter gets wrong: undo back one, then make a *different* edit. The
        // document no longer matches what was saved, so it must read dirty.
        history.Undo();
        history.Push(new SetAction("C", box, 1, 7, doc));
        Assert.True(history.IsDirty(doc));
    }

    [Fact]
    public void UndoingAllTheWayBackReportsCleanWhenNothingWasSaved()
    {
        var history = new UndoHistory();
        var doc = new Doc();
        int[] box = [0];

        history.Push(new SetAction("A", box, 0, 1, doc));
        Assert.True(history.IsDirty(doc));

        history.Undo();
        Assert.False(history.IsDirty(doc));
    }

    [Fact]
    public void DirtyTrackingIsPerDocument()
    {
        var history = new UndoHistory();
        var a = new Doc();
        var b = new Doc();
        int[] box = [0];

        history.Push(new SetAction("A", box, 0, 1, a));
        Assert.True(history.IsDirty(a));
        Assert.False(history.IsDirty(b));

        history.MarkSaved(a);
        history.Push(new SetAction("B", box, 1, 2, b));
        Assert.False(history.IsDirty(a));
        Assert.True(history.IsDirty(b));
    }

    [Fact]
    public void DiscardDocumentTruncatesFromTheOldestEntryTouchingIt()
    {
        var history = new UndoHistory();
        var closing = new Doc();
        var other = new Doc();
        int[] box = [0];

        history.Push(new SetAction("other-1", box, 0, 1, other));
        history.Push(new SetAction("closing-1", box, 1, 2, closing));
        history.Push(new SetAction("other-2", box, 2, 3, other));
        history.Push(new SetAction("closing-2", box, 3, 4, closing));

        int dropped = history.DiscardDocument(closing);

        // Everything from the first entry touching the closed document onwards goes, because the
        // remaining entries would no longer compose into a replayable sequence.
        Assert.Equal(3, dropped);
        Assert.Equal(1, history.Count);
        Assert.Equal(1, history.Cursor);
        Assert.Equal("other-1", history.UndoLabel);

        Assert.Equal(0, history.DiscardDocument(new Doc()));
    }

    [Fact]
    public void ClearResetsEverything()
    {
        var history = new UndoHistory();
        int[] box = [0];

        history.Push(new SetAction("A", box, 0, 1));
        history.Push(new SetAction("B", box, 1, 2));
        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.Equal(0, history.Cursor);
        Assert.Equal(0, history.TotalBytes);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void ChangedFiresOnPushUndoAndRedo()
    {
        var history = new UndoHistory();
        int[] box = [0];
        int fired = 0;
        history.Changed += () => fired++;

        history.Push(new SetAction("A", box, 0, 1));
        Assert.Equal(1, fired);

        history.Undo();
        Assert.Equal(2, fired);

        history.Redo();
        Assert.Equal(3, fired);
    }

    [Fact]
    public void AnActionCannotRecordAnotherWhileItIsBeingApplied()
    {
        var history = new UndoHistory();
        int[] box = [0];

        // A badly behaved action that tries to push while being undone must not corrupt the cursor.
        var reentrant = new DelegateProbe(
            () => history.Push(new SetAction("sneaky", box, 0, 1)),
            () => { });

        history.Push(reentrant);
        Assert.True(history.Undo());
        Assert.Equal(1, history.Count);
        Assert.Equal(0, history.Cursor);
    }
}
