using System.Numerics;
using Spot.Scenes;
using Spot.Rendering;

namespace Spot.Editor.Scenes;

public class DemoScene : Scene
{
    public override void OnEnter()
    {
        var camera = Instantiate("Main Camera");
        camera.AddComponent(new CameraComponent());
        
        var square = Instantiate("Square");
        square.AddComponent(new Sprite2DComponent { Color = new Vector4(0.2f, 0.8f, 0.3f, 1.0f) });
        square.AddScript<RotatorScript>();
    }
}

public class RotatorScript : EntityBehaviour
{
    public float Speed = 90.0f;

    public override void OnUpdate(float deltaTime)
    {
        var transform = GetComponent<TransformComponent>();
        var rotation = transform.Rotation;
        rotation.Z += Speed * deltaTime;
        transform.Rotation = rotation;
    }
}
