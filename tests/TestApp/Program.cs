using SpotEngine;

internal class Program
{
    public static void Main(string[] args)
    {
        Application app = Application.GetApp();
        app.Run();

        Entity e = new Entity();
        e.AddComponent(new Transform());
        //e.AddComponent(new SpriteRenderer());

        e.LoadComponents();

        app.GetWindow().Initialize();
    }
}