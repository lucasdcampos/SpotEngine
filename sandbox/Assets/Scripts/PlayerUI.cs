using System.Numerics;
using Spot.Rendering;
using Spot.Scenes;
using Spot.UI;

namespace Spot.Game;

/// <summary>
/// A sample heads-up display built with the engine's runtime UI (<c>Spot.UI</c>) instead of ImGui: a crosshair
/// at screen center and a health readout pinned to the bottom-left. It shows the retained UI layout — an
/// anchored panel with a large label plus a couple of thin bars — that any game can build from a script.
/// </summary>
public sealed class PlayerUI : EntityBehaviour
{
    public override void OnCreate()
    {
        BuildCrosshair();
        BuildHealthReadout();
    }

    // A simple '+' crosshair made of two thin bars crossing at screen center.
    private void BuildCrosshair()
    {
        var color = new Vector4(1f, 1f, 1f, 0.85f);
        AddCenteredBar(new Vector2(20f, 2f), color);
        AddCenteredBar(new Vector2(2f, 20f), color);
    }

    private void AddCenteredBar(Vector2 size, Vector4 color)
    {
        Panel bar = UI.Panel();
        bar.Color = color;
        bar.Rect = new UIRect
        {
            Anchor = new Vector2(0.5f, 0.5f),
            Pivot = new Vector2(0.5f, 0.5f),
            Position = Vector2.Zero,
            Size = size,
        };
    }

    // A translucent panel pinned to the bottom-left corner with a large Half-Life-orange health number.
    private void BuildHealthReadout()
    {
        Panel panel = UI.Panel();
        panel.Color = new Vector4(0f, 0f, 0f, 0.5f);
        panel.Rect = new UIRect
        {
            Anchor = new Vector2(0f, 1f),
            Pivot = new Vector2(0f, 1f),
            Position = new Vector2(40f, -40f),
            Size = new Vector2(240f, 92f),
        };

        Text hp = panel.Text("+ 100");
        hp.Color = new Vector4(1f, 0.6f, 0f, 1f);
        hp.FontSize = 52f;
        hp.Align = TextAlign.Left;
        hp.Rect = new UIRect
        {
            Anchor = Vector2.Zero,
            Pivot = Vector2.Zero,
            Position = new Vector2(28f, 18f),
            Size = new Vector2(200f, 60f),
        };
    }
}
