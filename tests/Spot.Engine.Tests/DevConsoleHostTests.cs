using Spot.Engine.Console;

namespace Spot.Engine.Tests;

public class DevConsoleHostTests
{
    [Fact]
    public void Standalone_ToggleOpensAndCloses()
    {
        var console = new DevConsole();
        Assert.False(console.IsHosted);
        Assert.False(console.IsOpen);

        console.Toggle();
        Assert.True(console.IsOpen);

        console.Toggle();
        Assert.False(console.IsOpen);
    }

    [Fact]
    public void Hosted_ToggleAsksTheHostInsteadOfOpeningAWindow()
    {
        var console = new DevConsole();
        int requests = 0;
        console.SetHost(() => requests++);

        Assert.True(console.IsHosted);

        // The host owns the window, so the console never reports itself open — that state is what the
        // application turns into engine input capture, and in the editor nothing could clear it.
        console.Toggle();
        Assert.Equal(1, requests);
        Assert.False(console.IsOpen);

        console.Toggle();
        Assert.Equal(2, requests);
        Assert.False(console.IsOpen);
    }

    [Fact]
    public void SetHost_ClearsAnAlreadyOpenWindowAndRestoresOnRelease()
    {
        var console = new DevConsole();
        console.Toggle();
        Assert.True(console.IsOpen);

        // A host taking over mid-session (the editor scene loading after the launcher) must not leave the
        // engine's open flag latched, or input stays captured with no window to dismiss.
        console.SetHost(() => { });
        Assert.False(console.IsOpen);

        console.SetHost(null);
        Assert.False(console.IsHosted);
        console.Toggle();
        Assert.True(console.IsOpen);
    }
}
