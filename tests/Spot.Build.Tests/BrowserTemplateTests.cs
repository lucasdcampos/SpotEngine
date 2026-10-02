using Spot.Build;

namespace Spot.Build.Tests;

/// <summary>
/// Pins the generated browser bootstrap's contract with the runtime: which assembly exports it binds to and
/// which entry points it calls, so a renamed export or a moved type fails here instead of in a live page.
/// </summary>
public class BrowserTemplateTests
{
    private static readonly string Js = BrowserTemplate.MainJs("Scenes/Main.sptscene");

    [Fact]
    public void MainJs_BindsTheEngineHostAndThePlatformExports()
    {
        Assert.Contains("const host = engineExports.Spot.Browser.BrowserHost;", Js);
        Assert.Contains("const platform = coreExports.Spot.Browser.BrowserPlatform;", Js);
    }

    [Fact]
    public void MainJs_RoutesFramesResizeAndInputThroughThePlatform()
    {
        foreach (string call in new[]
                 {
                     "platform.Frame(", "platform.Resize(", "platform.KeyDown(", "platform.KeyUp(", "platform.TextInput(",
                     "platform.PointerMove(", "platform.PointerMoveRelative(", "platform.PointerDown(",
                     "platform.PointerUp(", "platform.Wheel(",
                 })
        {
            Assert.Contains(call, Js);
        }

        Assert.DoesNotContain("host.Frame(", Js);
        Assert.DoesNotContain("host.KeyDown(", Js);
    }

    [Fact]
    public void MainJs_ReportsTheCanvasSizeBeforeBootingTheHost()
    {
        int resize = Js.IndexOf("platform.Resize(initW, initH);", StringComparison.Ordinal);
        int start = Js.IndexOf("await host.StartAsync(", StringComparison.Ordinal);

        Assert.True(resize >= 0 && start > resize);
    }

    [Fact]
    public void MainJs_EmbedsTheStartScene()
    {
        Assert.Contains("const START_SCENE = 'Scenes/Main.sptscene';", Js);
        Assert.DoesNotContain("__START_SCENE__", Js);
    }
}
