using Spot.Engine;
using Spot.Engine.Events;

namespace Spot.Engine.Tests;

public class InputTextTests
{
    private static void Type(string text)
    {
        foreach (char c in text)
        {
            Input.OnEvent(new KeyTypedEvent(c));
        }
    }

    [Fact]
    public void TypedText_CollectsTheFramesCharactersInOrder()
    {
        Input.Reset();
        Input.NewFrame();
        Type("Olá");
        Assert.Equal("Olá", Input.TypedText);

        // Read twice in a frame, then more typing: the text keeps growing until the next frame.
        Type("!");
        Assert.Equal("Olá!", Input.TypedText);
    }

    [Fact]
    public void TypedText_ClearsOnTheNextFrame()
    {
        Input.Reset();
        Input.NewFrame();
        Type("abc");
        Input.NewFrame();
        Assert.Equal(string.Empty, Input.TypedText);
    }

    [Fact]
    public void TypedText_SkipsControlCharacters()
    {
        Input.Reset();
        Input.NewFrame();
        Type("a\bb\r\n\tc");
        Assert.Equal("abc", Input.TypedText);
    }

    [Fact]
    public void TypedText_IsEmptyWhileInputIsBlocked()
    {
        Input.Reset();
        Input.NewFrame();
        Input.Captured = true;
        Type("secret");
        Assert.Equal(string.Empty, Input.TypedText);
        Input.Captured = false;
        Input.Reset();
    }

    [Fact]
    public void WindowSpec_DefaultsToWindowed()
    {
        Assert.Equal(WindowMode.Windowed, new WindowSpec().Mode);
    }
}
