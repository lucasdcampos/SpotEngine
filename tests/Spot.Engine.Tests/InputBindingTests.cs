using Spot.Core;

namespace Spot.Engine.Tests;

public class InputBindingTests
{
    [Theory]
    [InlineData("w", "w")]
    [InlineData("W", "w")]
    [InlineData("space", "space")]
    [InlineData("up", "up")]
    [InlineData("0", "0")]
    [InlineData("5", "5")]
    [InlineData("f1", "f1")]
    [InlineData("leftshift", "leftshift")]
    [InlineData("esc", "escape")]
    [InlineData("ctrl", "leftcontrol")]
    [InlineData("del", "delete")]
    [InlineData("mouse0", "mouse0")]
    [InlineData("lmb", "mouse0")]
    [InlineData("rmb", "mouse1")]
    [InlineData(",", "comma")]
    public void TryParse_ParsesTokenAndCanonicalizes(string token, string canonical)
    {
        Assert.True(InputBinding.TryParse(token, out InputBinding binding));
        Assert.Equal(canonical, binding.ToString());

        // The canonical token must parse back to the identical binding.
        Assert.True(InputBinding.TryParse(binding.ToString(), out InputBinding roundTripped));
        Assert.Equal(binding, roundTripped);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notakey")]
    [InlineData("unknown")]
    [InlineData("mouse9")]
    [InlineData("999")]
    public void TryParse_RejectsUnknownTokens(string? token)
    {
        Assert.False(InputBinding.TryParse(token, out _));
    }

    [Fact]
    public void Factories_ProduceExpectedBindings()
    {
        Assert.Equal(new InputBinding(InputDeviceKind.Keyboard, (int)Key.W), InputBinding.Key(Key.W));
        Assert.Equal(new InputBinding(InputDeviceKind.Mouse, (int)MouseButton.Left), InputBinding.Mouse(MouseButton.Left));
    }
}
