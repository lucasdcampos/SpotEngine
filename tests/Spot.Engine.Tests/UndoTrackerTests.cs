using Spot.DebugUI.Undo;

namespace Spot.Engine.Tests;

/// <summary>
/// Exercises the edit-boundary state machine without an ImGui context, by substituting the
/// interaction probe. These tests are the reason <see cref="UndoTracker.IsInteracting"/> is a
/// settable delegate rather than a direct ImGui call.
/// </summary>
public class UndoTrackerTests : IDisposable
{
    private readonly UndoHistory _history = new();
    private readonly UndoHistory _previousHistory = UndoTracker.History;
    private readonly Func<bool> _previousProbe = UndoTracker.IsInteracting;
    private bool _interacting;

    public UndoTrackerTests()
    {
        UndoTracker.History = _history;
        UndoTracker.IsInteracting = () => _interacting;
        UndoTracker.Abandon();
    }

    public void Dispose()
    {
        UndoTracker.Abandon();
        UndoTracker.History = _previousHistory;
        UndoTracker.IsInteracting = _previousProbe;
    }

    // Stands in for a component property being edited through a widget helper.
    private sealed class Box
    {
        public float Value;
    }

    private void Track(Box box, float before, bool changed, int idleMs = 0)
    {
        UndoTracker.Track(
            UndoKey.For(box, nameof(Box.Value)),
            "Set Value",
            before,
            () => box.Value,
            (b, a) => new ValueAction<float>("Set Value", v => box.Value = v, b, a),
            changed,
            idleMs);
    }

    [Fact]
    public void AContinuousDragBecomesExactlyOneEntry()
    {
        var box = new Box { Value = 0.0f };
        _interacting = true;

        // Three frames of dragging: the value moves each frame but the mouse is still down.
        for (int frame = 1; frame <= 3; frame++)
        {
            float before = box.Value;
            box.Value = frame;
            Track(box, before, changed: true);
            UndoTracker.EndFrame();
            Assert.Equal(0, _history.Count);
        }

        // Release.
        _interacting = false;
        UndoTracker.EndFrame();

        Assert.Equal(1, _history.Count);
        Assert.Equal("Set Value", _history.UndoLabel);

        // The single entry spans the whole drag: back to the value from before the first frame.
        _history.Undo();
        Assert.Equal(0.0f, box.Value);

        _history.Redo();
        Assert.Equal(3.0f, box.Value);
    }

    [Fact]
    public void ADiscreteChangeCommitsOnTheFollowingFrame()
    {
        var box = new Box { Value = 0.0f };
        _interacting = false;

        box.Value = 1.0f;
        Track(box, before: 0.0f, changed: true);
        UndoTracker.EndFrame();

        Assert.Equal(1, _history.Count);
        _history.Undo();
        Assert.Equal(0.0f, box.Value);
    }

    [Fact]
    public void NoChangeRecordsNothing()
    {
        var box = new Box { Value = 2.0f };
        _interacting = false;

        Track(box, before: 2.0f, changed: false);
        UndoTracker.EndFrame();

        Assert.Equal(0, _history.Count);
        Assert.False(UndoTracker.HasPending);
    }

    [Fact]
    public void AnEditThatEndsWhereItStartedIsDiscarded()
    {
        var box = new Box { Value = 5.0f };
        _interacting = true;

        // Drag away...
        box.Value = 9.0f;
        Track(box, before: 5.0f, changed: true);
        UndoTracker.EndFrame();

        // ...and back again (this is also how an Escape-cancelled text edit resolves).
        box.Value = 5.0f;
        Track(box, before: 9.0f, changed: true);
        UndoTracker.EndFrame();

        _interacting = false;
        UndoTracker.EndFrame();

        Assert.Equal(0, _history.Count);
    }

    [Fact]
    public void MovingToADifferentFieldCommitsThePreviousEdit()
    {
        var first = new Box { Value = 0.0f };
        var second = new Box { Value = 0.0f };
        _interacting = true;

        first.Value = 1.0f;
        Track(first, before: 0.0f, changed: true);

        // Focus jumps straight to another field without an idle frame in between (Tab, or a click
        // directly onto it). The first edit must not be lost.
        second.Value = 2.0f;
        Track(second, before: 0.0f, changed: true);

        Assert.Equal(1, _history.Count);

        _interacting = false;
        UndoTracker.EndFrame();

        Assert.Equal(2, _history.Count);
    }

    [Fact]
    public void FlushCommitsImmediatelyEvenMidInteraction()
    {
        var box = new Box { Value = 0.0f };
        _interacting = true;

        box.Value = 4.0f;
        Track(box, before: 0.0f, changed: true);
        Assert.Equal(0, _history.Count);

        UndoTracker.Flush();

        Assert.Equal(1, _history.Count);
        Assert.False(UndoTracker.HasPending);
    }

    [Fact]
    public void PushingAStructuralActionCommitsThePendingEditFirst()
    {
        var box = new Box { Value = 0.0f };
        _interacting = true;

        box.Value = 4.0f;
        Track(box, before: 0.0f, changed: true);

        // A structural action arriving mid-edit must land *after* the value edit, not before it.
        UndoTracker.Flush();
        _history.Push(new ValueAction<float>("Structural", _ => { }, 0.0f, 1.0f));

        Assert.Equal(2, _history.Count);
        Assert.Equal("Structural", _history.UndoLabel);
        Assert.Equal("Set Value", _history.LabelAt(0));
    }

    [Fact]
    public void AbandonDropsThePendingEditWithoutRecordingIt()
    {
        var box = new Box { Value = 0.0f };
        _interacting = true;

        box.Value = 7.0f;
        Track(box, before: 0.0f, changed: true);
        Assert.True(UndoTracker.HasPending);

        UndoTracker.Abandon();
        _interacting = false;
        UndoTracker.EndFrame();

        Assert.Equal(0, _history.Count);
    }

    [Fact]
    public void KeyboardRepeatsCollapseWithinTheIdleWindow()
    {
        var box = new Box { Value = 0.0f };

        // Keyboard-driven edits leave nothing active, so without an idle window each arrow press would
        // become its own entry.
        _interacting = false;

        box.Value = 1.0f;
        Track(box, before: 0.0f, changed: true, idleMs: 400);
        UndoTracker.EndFrame();
        Assert.Equal(0, _history.Count);

        box.Value = 2.0f;
        Track(box, before: 1.0f, changed: true, idleMs: 400);
        UndoTracker.EndFrame();
        Assert.Equal(0, _history.Count);

        Assert.True(UndoTracker.HasPending);
    }

    [Fact]
    public void AnIdleWindowEditCommitsOnceTheWindowElapses()
    {
        var box = new Box { Value = 0.0f };
        _interacting = false;

        box.Value = 1.0f;
        Track(box, before: 0.0f, changed: true, idleMs: 1);
        UndoTracker.EndFrame();

        Thread.Sleep(20);
        UndoTracker.EndFrame();

        Assert.Equal(1, _history.Count);
        _history.Undo();
        Assert.Equal(0.0f, box.Value);
    }

    [Fact]
    public void ASuspendedHistoryRecordsNothingTheTrackerSees()
    {
        var box = new Box { Value = 0.0f };
        _interacting = false;

        using (_history.Suspend())
        {
            box.Value = 3.0f;
            Track(box, before: 0.0f, changed: true);
            UndoTracker.EndFrame();
        }

        Assert.Equal(0, _history.Count);
    }
}
