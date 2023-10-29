using SpotEngine;

internal class Program
{
    public static void Main(string[] args)
    {
        Application app = Application.GetApp();

        Scene scene = new Scene();

        Entity test = new Entity();
        test.AddComponent(new SpriteRenderer());
        scene.entities.Add(test);
        Scene.LoadScene(scene);


        app.Run();

    }
}