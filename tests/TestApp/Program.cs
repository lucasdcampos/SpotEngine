using SpotEngine;

internal class Program
{
    public static void Main(string[] args)
    {
        Application app = Application.GetApp();
        app.Run();

        Scene scene = new Scene();

        Entity e = new Entity();
        e.AddComponent<Transform>();
        e.AddComponent<SpriteRenderer>();

        Scene.current = scene;
        scene.RegisterEntity(e);

        e.transform.scale = new Vec3(0.5f, 0.5f,0.5f);

        app.GetWindow().Initialize();
    }
}