using SpotEngine;
using Game;

internal class Program
{
    public static void Main(string[] args)
    {
        var app = Application.GetApp();
        app.ChangeScene(new FirstPersonCamera());
        app.Run();
    }
}