using System.Numerics;
using Spot.Scenes;

namespace Spot.Editor.Scenes;

public class DemoScene : Scene
{
    public override void OnEnter()
    {
        var square = Instantiate("Square");
        square.AddComponent(new Sprite2D { Color = new Vector4(0.2f, 0.8f, 0.3f, 1.0f) });
    }
}
