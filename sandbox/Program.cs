using System;
using System.IO;
using Spot.Core;
using Spot.Scenes;
using Spot.Assets;

namespace Sandbox;

class LoaderScene : Scene
{
    public override void OnEnter()
    {
        AssetPath.Root = "Assets";
        string startScenePath = Path.Combine(AssetPath.Root, "Scenes/Main.sptscene");
        if (File.Exists(startScenePath))
        {
            var realScene = new Scene();
            var serializer = new SceneSerializer(realScene);
            serializer.Deserialize(startScenePath);
            SceneManager.Load(realScene);
        }
        else
        {
            Log.Error($"Start scene not found: {startScenePath}");
        }
    }
}

class Program
{
    static void Main(string[] args)
    {
        var spec = new ApplicationSpec
        {
            Name = "Sandbox"
        };
        spec.Window.Title = "Sandbox";
        spec.Window.Width = 1280;
        spec.Window.Height = 720;

        var app = new Application(spec);
        app.Run(new LoaderScene());
    }
}
