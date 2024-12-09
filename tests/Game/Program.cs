using SpotEngine;
using SpotEngine.Internal.Graphics;
using System.Text.RegularExpressions;

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
    public float zoom = 20;
    public float gravity = 10;
    Transform transform = new Transform();
    Actor player = new Actor();
    List<Actor> clouds = new List<Actor>();

    protected override void OnStart()
    {
        AddActor(player);
       
        player.transform.SetTotalScale(1);
        player.transform.Pos.Y += 1.5f;
        player.SpriteRenderer.Sprite.color = new Color(200, 50, 0, 255);
        BackgroundColor = new Color(100, 150, 255, 255);
    }

    float startCountdown = 1.5f;
    float countdown = 0;
    protected override void OnUpdate(float dt)
    {
        float speed = 6 * dt;

        Vec3 cameraPos = new Vec3(player.X, player.Y, zoom);
        renderer.SetCameraPosition(cameraPos);
        ApplyGravity();
       
        if (Input.IsKeyPressed(KeyCode.D)) player.transform.Pos.X += speed;
        if (Input.IsKeyPressed(KeyCode.A)) player.transform.Pos.X -= speed;

        if (Input.IsKeyPressed(KeyCode.W)) cameraPos.Z += speed;
        if (Input.IsKeyPressed(KeyCode.S)) cameraPos.Z -= speed;

        if(countdown <= 0)
        {
            SpawnCloud();
            countdown = startCountdown;
        }

        countdown -= dt;

    }

    private void SpawnCloud()
    {
        var cloud = new Cloud(player.transform.Pos.Y);
        clouds.Add(cloud);
        AddActor(cloud);
    }

    float accel = 0;
    void ApplyGravity()
    {
        foreach(var c in clouds)
        {
            if (player.CollidesWith(c))
            {
                accel = 0;
                return;
            }
        }

        //accel += 0.005f * Time.deltaTime;
        player.transform.Pos.Y -= gravity * Time.deltaTime + accel;
    }

}

class Cloud : Actor
{
    public Cloud(float y)
    {
        transform.Pos = new Vec3(Rand.Between(-2.5f, 2.5f), Rand.Between(y-10, y-15f), 0);
        transform.Scale.X = Rand.Between(3, 5);
    }
}