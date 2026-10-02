using Spot.Core;

namespace Spot.Engine.Tests;

/// <summary>
/// The browser's DOM-code mapping, which only ever runs in WebAssembly, is plain logic compiled for every target
/// so it can be pinned here.
/// </summary>
public class DomInputTests
{
    [Theory]
    [InlineData("KeyA", Key.A)]
    [InlineData("KeyZ", Key.Z)]
    [InlineData("Digit0", Key.Alpha0)]
    [InlineData("Digit9", Key.Alpha9)]
    [InlineData("F1", Key.F1)]
    [InlineData("F12", Key.F12)]
    [InlineData("Space", Key.Space)]
    [InlineData("NumpadEnter", Key.Enter)]
    [InlineData("ArrowUp", Key.Up)]
    [InlineData("ShiftRight", Key.RightShift)]
    [InlineData("Quote", Key.Apostrophe)]
    public void MapKey_TranslatesDomCodes(string code, Key expected)
    {
        Assert.Equal(expected, DomInput.MapKey(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Keya")]      // lowercase letter is not a DOM code
    [InlineData("KeyAB")]
    [InlineData("Digit10")]
    [InlineData("F13")]
    [InlineData("F0")]
    [InlineData("Fn")]
    [InlineData("MediaPlayPause")]
    public void MapKey_RejectsUnknownCodes(string? code)
    {
        Assert.Equal(Key.Unknown, DomInput.MapKey(code));
    }

    [Theory]
    [InlineData(0, MouseButton.Left)]
    [InlineData(1, MouseButton.Middle)]
    [InlineData(2, MouseButton.Right)]
    [InlineData(3, MouseButton.Button4)]
    [InlineData(4, MouseButton.Button5)]
    [InlineData(5, MouseButton.Unknown)]
    [InlineData(-1, MouseButton.Unknown)]
    public void MapButton_TranslatesDomButtons(int button, MouseButton expected)
    {
        Assert.Equal(expected, DomInput.MapButton(button));
    }
}
