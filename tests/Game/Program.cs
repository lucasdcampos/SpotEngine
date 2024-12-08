using SpotEngine;
using SpotEngine.Internal.Graphics;

internal class Program
{
    public static void Main(string[] args)
    {
        var app = Application.GetApp();
        app.ChanceScene(new MainScene());
        app.Run();
    }
}

public class MainScene : Scene
{
    Transform transform = new Transform();
    Vec3 cameraPos = new Vec3(0, 0, 3f);
    Actor Actor = new Actor();
    protected override void OnStart()
    {
        AddActor(Actor);
        transform.SetTotalScale(0.5f);
    }

    protected override void OnUpdate(float dt)
    {
        renderer.SetCameraPosition(cameraPos);

        float speed = 2 * dt;

        if (Input.IsKeyPressed(KeyCode.D)) cameraPos.X += speed;
        if (Input.IsKeyPressed(KeyCode.A)) cameraPos.X -= speed;
        if (Input.IsKeyPressed(KeyCode.W)) cameraPos.Z += speed;
        if (Input.IsKeyPressed(KeyCode.S)) cameraPos.Z -= speed;

        if (Input.IsKeyPressed(KeyCode.K)) transform.Rot.Y += speed * 50;
        if (Input.IsKeyPressed(KeyCode.L)) transform.Rot.Y -= speed * 50;
    }

    protected override void OnRender(float dt)
    {
        base.OnRender(dt);
    }
}