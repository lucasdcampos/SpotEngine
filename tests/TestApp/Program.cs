using SpotEngine;

internal class Program
{
    public static void Main(string[] args)
    {
        Application app = Application.GetApp();
        app.CreateWindow(Application.RenderMode.OpenTK, "Spot Engine Editor", new Vec2(800, 600));

        Scene scene = new Scene();
        scene.name = "BlankScene";

        Entity e = new Entity();
        e.name = "Player";
        e.AddComponent<SpriteRenderer>();
        Sprite spr = e.GetComponent<SpriteRenderer>().sprite = new Sprite("Player.png", Color.White);
        spr.layer = 10;
        e.AddComponent<Collider2D>();
        e.AddComponent<Rigidbody2D>();
        e.AddComponent<TestClass>();
        e.transform.scale = new Vec3(1f, 1f, 1f);
        e.transform.pos = new Vec3(0, 2, 0);
        e.GetComponent<Rigidbody2D>().Velocity = new Vec2(0, 0);
        scene.RegisterEntity(e);

        Entity square = new Entity();
        square.name = "Square";
        square.AddComponent<SpriteRenderer>();
        square.AddComponent<Collider2D>();
        square.transform.pos = new Vec3(0, -2, 0);
        square.transform.scale = new Vec3(10, 1, 1);
        scene.RegisterEntity(square);

        Entity square2 = new Entity();
        square2.name = "Square";
        square2.AddComponent<SpriteRenderer>().sprite.color = Color.Red;
        square2.transform.scale = new Vec3(2f,0.3f,0);
        square2.transform.pos = new Vec3(-2.75f, 2, 0);
        scene.RegisterEntity(square2);

        Entity manaBar = new Entity();
        manaBar.name = "UI_ManaBar";
        manaBar.AddComponent<SpriteRenderer>().sprite.color = Color.Blue;
        manaBar.transform.scale = new Vec3(2f, 0.3f, 0);
        manaBar.transform.pos = new Vec3(-2.75f, 1.6f, 0);
        scene.RegisterEntity(manaBar);
        Scene.SaveScene(scene);
        Scene.LoadScene(scene);
        app.Run();
    }
}

internal class TestClass : Component
{
    public override void OnUpdate()
    {
        base.OnUpdate();

        if (Input.IsKeyPressed(KeyCode.Escape))
        {
            Scene.LoadScene(Scene.current);
        }

    }
}