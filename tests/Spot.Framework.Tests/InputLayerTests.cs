using System.Numerics;
using Spot.Framework;
using Spot.Framework.Events;

namespace Spot.Framework.Tests;

/// <summary>
/// Covers the raw <see cref="Input"/> mechanisms exposed to every level (capture, suppression, cursor control,
/// gamepad axes, frame/reset events) and the named-action layer built on top of them.
/// </summary>
public class InputLayerTests : IDisposable
{
    private readonly FakeCursor _cursor = new();

    public InputLayerTests()
    {
        Input.Reset();
        Input.CursorController = _cursor;
        Input.CursorLocked = false;
    }

    public void Dispose()
    {
        Input.Reset();
        Input.CursorController = null;
    }

    private static void Press(Key key) => Input.OnEvent(new KeyPressedEvent(key));

    private static void Axis(int pad, GamepadAxis axis, float value) =>
        Input.OnEvent(new GamepadAxisMovedEvent(pad, axis, value));

    [Fact]
    public void Captured_ForcesTheCursorFreeAndRestoresTheAppsRequest()
    {
        Input.CursorLocked = true;
        Assert.True(_cursor.Locked);

        Input.Captured = true;
        Assert.False(_cursor.Locked);
        Assert.False(Input.CursorLocked);
        Assert.True(Input.IsBlocked);

        Input.Captured = false;
        Assert.True(_cursor.Locked);
        Assert.False(Input.IsBlocked);
    }

    [Fact]
    public void CursorRequestWhileCaptured_IsAppliedWhenTheCaptureEnds()
    {
        Input.Captured = true;
        Input.CursorLocked = true;
        Assert.False(_cursor.Locked);

        Input.Captured = false;
        Assert.True(_cursor.Locked);
    }

    [Fact]
    public void Suppressed_BlocksReadsWithoutTouchingTheCursor()
    {
        Input.CursorLocked = true;
        Input.NewFrame();
        Press(Key.Space);

        Input.Suppressed = true;
        Assert.True(_cursor.Locked);
        Assert.False(Input.GetKey(Key.Space));

        Input.Suppressed = false;
        Assert.True(Input.GetKey(Key.Space));
    }

    [Fact]
    public void ReleaseAndRestoreCursor_KeepTheAppsRequest()
    {
        Input.CursorLocked = true;

        Input.ReleaseCursor();
        Assert.False(_cursor.Locked);

        Input.RestoreCursor();
        Assert.True(_cursor.Locked);
    }

    [Fact]
    public void TickCursorLock_TicksTheController()
    {
        Input.TickCursorLock();
        Input.TickCursorLock();

        Assert.Equal(2, _cursor.Ticks);
    }

    [Fact]
    public void RelativeMouseMode_IgnoresAbsoluteMovesAndAccumulatesMotion()
    {
        Input.OnEvent(new MouseMovedEvent(10, 10));
        Input.RelativeMouseMode = true;

        Input.OnEvent(new MouseMovedEvent(500, 500));
        Input.AddMouseMotion(new Vector2(3, -2));

        Assert.Equal(new Vector2(13, 8), Input.MousePosition);
    }

    [Fact]
    public void GamepadAxis_ReportsTheStrongestPadAndThePreviousFrame()
    {
        Axis(0, GamepadAxis.LeftX, 0.25f);
        Axis(1, GamepadAxis.LeftX, -0.75f);

        Assert.Equal(-0.75f, Input.GetGamepadAxis(GamepadAxis.LeftX));
        Assert.Equal(0.25f, Input.GetGamepadAxis(0, GamepadAxis.LeftX));
        Assert.Equal(0f, Input.GetPreviousGamepadAxis(GamepadAxis.LeftX));

        Input.NewFrame();
        Axis(1, GamepadAxis.LeftX, 0f);

        Assert.Equal(-0.75f, Input.GetPreviousGamepadAxis(GamepadAxis.LeftX));
        Assert.Equal(0.25f, Input.GetGamepadAxis(GamepadAxis.LeftX));
    }

    [Fact]
    public void GamepadAxis_ReadsZeroWhileBlocked()
    {
        Axis(0, GamepadAxis.RightTrigger, 1f);
        Input.Suppressed = true;

        Assert.Equal(0f, Input.GetGamepadAxis(GamepadAxis.RightTrigger));
        Assert.Equal(0f, Input.GetGamepadAxis(0, GamepadAxis.RightTrigger));
    }

    [Fact]
    public void NewFrameAndReset_RaiseTheirEvents()
    {
        int frames = 0, clears = 0;
        void OnFrame() => frames++;
        void OnClear() => clears++;
        Input.FrameStarted += OnFrame;
        Input.Cleared += OnClear;
        try
        {
            Input.NewFrame();
            Input.NewFrame();
            Input.Reset();
        }
        finally
        {
            Input.FrameStarted -= OnFrame;
            Input.Cleared -= OnClear;
        }

        Assert.Equal((2, 1), (frames, clears));
    }

    [Fact]
    public void Reset_ClearsHeldInputAndBlocks()
    {
        Press(Key.A);
        Input.Captured = true;

        Input.Reset();

        Assert.False(Input.IsBlocked);
        Input.NewFrame();
        Assert.False(Input.GetKey(Key.A));
    }

    [Fact]
    public void Actions_AreTheSameThroughInputAndInputActions()
    {
        Input.Bind("jump", Key.Space);
        Input.NewFrame();
        Press(Key.Space);

        Assert.True(Input.GetAction("jump"));
        Assert.True(InputActions.GetAction("jump"));
        Assert.True(InputActions.GetActionDown("jump"));
        Assert.Equal(Input.GetBindings("jump"), InputActions.GetBindings("jump"));
    }

    [Fact]
    public void Actions_GamepadAxisActsAsAButtonPastHalfDeflection()
    {
        Input.Bind("accelerate", InputBinding.Gamepad(GamepadAxis.RightTrigger));

        Input.NewFrame();
        Axis(0, GamepadAxis.RightTrigger, 0.4f);
        Assert.False(Input.GetAction("accelerate"));

        Input.NewFrame();
        Axis(0, GamepadAxis.RightTrigger, 0.9f);
        Assert.True(Input.GetActionDown("accelerate"));
        Assert.True(Input.GetAction("accelerate"));

        Input.NewFrame();
        Assert.True(Input.GetAction("accelerate"));
        Assert.False(Input.GetActionDown("accelerate"));

        Input.NewFrame();
        Axis(0, GamepadAxis.RightTrigger, 0.1f);
        Assert.True(Input.GetActionUp("accelerate"));
        Assert.False(Input.GetAction("accelerate"));
    }

    [Fact]
    public void Actions_SetActionStateEdgesLastOneFrame()
    {
        Input.NewFrame();
        Input.SetActionState("fire", true);
        Assert.True(Input.GetActionDown("fire"));
        Assert.True(Input.GetAction("fire"));

        Input.NewFrame();
        Assert.False(Input.GetActionDown("fire"));
        Assert.True(Input.GetAction("fire"));

        Input.SetActionState("fire", false);
        Assert.True(Input.GetActionUp("fire"));

        Input.NewFrame();
        Assert.False(Input.GetActionUp("fire"));
        Assert.False(Input.GetAction("fire"));
    }

    [Fact]
    public void Actions_ReportNothingWhileBlocked()
    {
        Input.Bind("jump", Key.Space);
        Input.NewFrame();
        Press(Key.Space);
        Input.SetActionState("fire", true);

        Input.Captured = true;

        Assert.False(Input.GetAction("jump"));
        Assert.False(Input.GetActionDown("jump"));
        Assert.False(Input.GetAction("fire"));
    }

    [Fact]
    public void Reset_AlsoClearsActionsAndDefaults()
    {
        Input.SetDefaultBindings(new Dictionary<string, InputBinding[]> { ["jump"] = new[] { InputBinding.Key(Key.Space) } });
        Input.SetActionState("fire", true);

        Input.Reset();
        Input.ResetBindingsToDefaults();

        Assert.Empty(Input.GetActionNames());
        Assert.False(Input.GetAction("fire"));
    }

    private sealed class FakeCursor : ICursorController
    {
        public bool Locked { get; set; }

        public int Ticks { get; private set; }

        public void Tick() => Ticks++;
    }
}
