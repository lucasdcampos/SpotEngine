using System.Numerics;
using Spot.Framework;
using Spot.Framework.Events;

namespace Spot.Framework.Tests;

/// <summary>
/// Covers presenting the app in a rectangle of the window (an editor viewport): the view's size replaces the
/// surface's, and the mouse is measured from the view's corner.
/// </summary>
public class DisplayViewTests : IDisposable
{
    public DisplayViewTests()
    {
        Input.Reset();
        Display.ClearView();
        Display.SetSize(1600, 900);
    }

    public void Dispose()
    {
        Display.ClearView();
        Input.Reset();
    }

    [Fact]
    public void WithoutAView_TheDisplayIsTheSurfaceAndTheMouseIsInWindowPixels()
    {
        Input.OnEvent(new MouseMovedEvent(500, 300));

        Assert.False(Display.HasView);
        Assert.Equal((1600, 900), (Display.Width, Display.Height));
        Assert.Equal(Vector2.Zero, Display.ViewOrigin);
        Assert.Equal(new Vector2(500, 300), Input.MousePosition);
    }

    [Fact]
    public void AView_ReportsItsSizeAndMeasuresTheMouseFromItsCorner()
    {
        Input.OnEvent(new MouseMovedEvent(500, 300));
        Display.SetView(400, 100, 800, 600);

        Assert.True(Display.HasView);
        Assert.Equal((800, 600), (Display.Width, Display.Height));
        Assert.Equal(new Vector2(100, 200), Input.MousePosition);

        // Outside the view the position simply falls outside its bounds.
        Input.OnEvent(new MouseMovedEvent(10, 20));
        Assert.Equal(new Vector2(-390, -80), Input.MousePosition);
    }

    [Fact]
    public void ClearingTheView_RestoresTheSurfaceAndWindowPixels()
    {
        Input.OnEvent(new MouseMovedEvent(500, 300));
        Display.SetView(400, 100, 800, 600);
        Display.ClearView();

        Assert.Equal((1600, 900), (Display.Width, Display.Height));
        Assert.Equal(new Vector2(500, 300), Input.MousePosition);
    }

    [Fact]
    public void ResizingTheSurface_KeepsTheViewUntilItIsCleared()
    {
        Display.SetView(400, 100, 800, 600);
        Display.SetSize(1280, 720);
        Assert.Equal((800, 600), (Display.Width, Display.Height));

        Display.ClearView();
        Assert.Equal((1280, 720), (Display.Width, Display.Height));
    }

    [Fact]
    public void AFrozenMousePosition_IsMeasuredFromTheViewToo()
    {
        Input.OnEvent(new MouseMovedEvent(500, 300));
        Input.Suppressed = true;
        Input.OnEvent(new MouseMovedEvent(900, 800));
        Display.SetView(400, 100, 800, 600);

        Assert.Equal(new Vector2(100, 200), Input.MousePosition);
    }

    [Fact]
    public void AView_NeverReportsADegenerateSize()
    {
        Display.SetView(0, 0, 0, -5);
        Assert.Equal((1, 1), (Display.Width, Display.Height));
    }
}
