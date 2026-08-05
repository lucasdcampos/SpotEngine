using System;
using System.IO;
using Spot.Assets;
using Spot.Core;

namespace Sandbox;

class Program
{
    static void Main(string[] args)
    {
        var spec = new ApplicationSpec
        {
            Name = "Sandbox",
            StartScene = "Scenes/Main.sptscene",
        };
        spec.Window.Title = "Sandbox";
        spec.Window.Width = 1280;
        spec.Window.Height = 720;

        string contentDir = Path.GetFullPath("Content");
        if (File.Exists(Path.Combine(contentDir, "manifest.json")))
        {
            // A cooked build sits next to us: load cooked content, exactly like a shipped game.
            spec.ContentDirectory = contentDir;
            spec.ManifestPath = "manifest.json";
        }
        else
        {
            // Dev run from source: index assets and cook on demand into a local Library, so guid:
            // references resolve to the same cooked artifacts a build would ship.
            string assetsRoot = Path.GetFullPath("Assets");
            AssetDatabase.Refresh(assetsRoot);
            AssetDatabase.InstallLibraryResolver(Path.GetFullPath("Library"));
            spec.AssetDirectory = assetsRoot;
        }

        var app = new Application(spec);
        app.Run();
    }
}
