using System;
using Spot.Core;

namespace Sandbox;

class Program
{
    static void Main(string[] args)
    {
        var spec = new ApplicationSpec
        {
            Name = "Sandbox",
            AssetDirectory = "Assets",
            StartScene = "Scenes/Main.sptscene"
        };
        spec.Window.Title = "Sandbox";
        spec.Window.Width = 1280;
        spec.Window.Height = 720;

        var app = new Application(spec);
        app.Run();
    }
}
