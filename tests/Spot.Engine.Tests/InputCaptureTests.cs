using System.Numerics;
using Spot.Core;
using Spot.Events;

namespace Spot.Engine.Tests;

public class InputCaptureTests
{
    private static void Move(float x, float y) => Input.OnEvent(new MouseMovedEvent(x, y));

    [Fact]
    public void EngineCapture_FreezesMousePositionAndRestoresIt()
    {
        Input.ResetForTests();
        Input.NewFrame();
        Move(100.0f, 100.0f);
        Assert.Equal(new Vector2(100.0f, 100.0f), Input.MousePosition);

        // Console opens: the cursor is freed and roams, but the game must see a still mouse so its
        // frame-to-frame delta stays zero and the camera does not spin.
        Input.SetEngineCaptured(true);
        Input.NewFrame();
        Move(400.0f, 250.0f);
        Assert.Equal(new Vector2(100.0f, 100.0f), Input.MousePosition);

        // Console closes: still the frozen value, so the first delta back is zero.
        Input.SetEngineCaptured(false);
        Assert.Equal(new Vector2(100.0f, 100.0f), Input.MousePosition);

        Input.NewFrame();
        Move(110.0f, 100.0f);
        Assert.Equal(new Vector2(110.0f, 100.0f), Input.MousePosition);
    }

    [Fact]
    public void EngineCapture_IgnoresLockedCursorMotion()
    {
        Input.ResetForTests();
        Input.AddMouseMotion(new Vector2(10.0f, 0.0f));
        Assert.Equal(new Vector2(10.0f, 0.0f), Input.MousePosition);

        Input.SetEngineCaptured(true);
        Input.AddMouseMotion(new Vector2(50.0f, 50.0f));
        Assert.Equal(new Vector2(10.0f, 0.0f), Input.MousePosition);

        Input.SetEngineCaptured(false);
        Assert.Equal(new Vector2(10.0f, 0.0f), Input.MousePosition);
    }

    [Fact]
    public void OverlappingCaptures_StayFrozenUntilTheLastOneEnds()
    {
        Input.ResetForTests();
        Input.NewFrame();
        Move(60.0f, 60.0f);

        Input.GameInputSuppressed = true;
        Input.SetEngineCaptured(true);
        Input.NewFrame();
        Move(500.0f, 500.0f);

        // Only one of the two blocks lifted — the position must stay frozen.
        Input.SetEngineCaptured(false);
        Assert.Equal(new Vector2(60.0f, 60.0f), Input.MousePosition);

        Input.GameInputSuppressed = false;
        Assert.Equal(new Vector2(60.0f, 60.0f), Input.MousePosition);

        Input.NewFrame();
        Move(65.0f, 60.0f);
        Assert.Equal(new Vector2(65.0f, 60.0f), Input.MousePosition);
    }

    [Fact]
    public void EngineCapture_WithholdsKeyboardAndMouseButtons()
    {
        Input.ResetForTests();
        Input.NewFrame();
        Input.OnEvent(new KeyPressedEvent(Key.W));
        Input.OnEvent(new MouseButtonPressedEvent(MouseButton.Left));
        Input.OnEvent(new MouseScrolledEvent(0.0f, 1.0f));
        Assert.True(Input.GetKey(Key.W));
        Assert.True(Input.GetMouseButton(MouseButton.Left));
        Assert.Equal(1.0f, Input.MouseScrollDelta.Y);

        Input.SetEngineCaptured(true);
        Assert.False(Input.GetKey(Key.W));
        Assert.False(Input.GetMouseButton(MouseButton.Left));
        Assert.Equal(0.0f, Input.MouseScrollDelta.Y);
    }
}
