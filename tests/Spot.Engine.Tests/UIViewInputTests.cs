using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Engine.UI;
using Spot.Framework;
using Spot.Framework.Events;

namespace Spot.Engine.Tests;

/// <summary>
/// The scene's UI routes the pointer in the app's view: when a host (the editor's play viewport) presents the game
/// in a rectangle of its window, a click lands on the widget drawn under it, not on one offset by the rectangle.
/// </summary>
public class UIViewInputTests : IDisposable
{
    private const float Dt = 1f / 60f;

    public UIViewInputTests()
    {
        Input.Reset();
        Display.ClearView();
        Display.SetSize(1920, 1080);
    }

    public void Dispose()
    {
        Display.ClearView();
        Input.Reset();
    }

    private static (Scene Scene, Func<int> Clicks) SceneWithButton()
    {
        var scene = new Scene();
        int clicks = 0;
        scene.UI.ScaleMode = UIScaleMode.ConstantPixel;
        Button button = scene.UI.Button("Go");
        button.Rect = new UIRect { Anchor = Vector2.Zero, Pivot = Vector2.Zero, Position = new Vector2(50, 50), Size = new Vector2(100, 40) };
        button.OnClick += () => clicks++;
        return (scene, () => clicks);
    }

    private static void Click(Scene scene, Vector2 windowPoint)
    {
        Input.NewFrame();
        Input.OnEvent(new MouseMovedEvent(windowPoint.X, windowPoint.Y));
        Input.OnEvent(new MouseButtonPressedEvent(MouseButton.Left));
        scene.UpdateRuntime(Dt);

        Input.NewFrame();
        Input.OnEvent(new MouseButtonReleasedEvent(MouseButton.Left));
        scene.UpdateRuntime(Dt);
    }

    [Fact]
    public void AClickInsideAView_ReachesTheButtonDrawnThere()
    {
        (Scene scene, Func<int> clicks) = SceneWithButton();
        Display.SetView(300, 200, 800, 600);

        Click(scene, new Vector2(300 + 60, 200 + 60));

        Assert.Equal(1, clicks());
    }

    [Fact]
    public void AClickAtTheButtonsWindowPosition_MissesItWhenTheViewIsOffset()
    {
        (Scene scene, Func<int> clicks) = SceneWithButton();
        Display.SetView(300, 200, 800, 600);

        Click(scene, new Vector2(60, 60));

        Assert.Equal(0, clicks());
    }

    [Fact]
    public void ScaleWithHeight_FollowsTheViewsHeight()
    {
        var scene = new Scene();
        scene.UI.ReferenceHeight = 1080;
        scene.UI.Panel();
        Display.SetView(0, 0, 960, 540);

        scene.UpdateRuntime(Dt);

        Assert.Equal(0.5f, scene.UI.Scale, 3);
        Assert.Equal(1920f, scene.UI.Width, 1);
    }
}
