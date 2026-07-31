using System;
using System.IO;
using Spot.Core;
using Spot.Build;
using Spot.Scenes;

namespace Sandbox;

/// <summary>
/// Entry point for the Spot sandbox: a small, data-driven reference project meant to be opened and
/// edited in the Spot editor and also run standalone. It boots the same way a shipped Spot game does —
/// load the <c>.sptproj</c>, then deserialize and switch to its configured start scene.
/// </summary>
internal static class Program
{
    private static void Main()
    {
        var spec = new ApplicationSpec { Name = "Spot Sandbox" };
        spec.Window.Title = "Spot Sandbox";
        spec.Window.Width = 1280;
        spec.Window.Height = 720;

        var app = new Application(spec);
        app.Run(new BootScene());
    }
}

/// <summary>
/// Loads the sandbox project and switches to its start scene. Deliberately tiny and defensive: any
/// failure is logged, never thrown, so the window still opens (honoring the engine's never-crash rule).
/// </summary>
internal sealed class BootScene : Scene
{
    public override void OnEnter()
    {
        // Resolve next to the executable (not the working directory) so `dotnet run` from anywhere and
        // published builds behave the same. Loading the project also points the engine's asset resolver
        // at this project's Assets/ directory, making the scenes' relative texture/material paths work.
        string projectPath = Path.Combine(AppContext.BaseDirectory, "Sandbox.sptproj");
        if (Project.Load(projectPath) is null || Project.Active is null)
        {
            Log.Error("Sandbox: could not load project '{0}'.", projectPath);
            return;
        }

        string scenePath = Path.Combine(Project.Active.GetAssetDirectory(), Project.Active.Config.StartScene);
        var scene = new Scene();
        if (new SceneSerializer(scene).Deserialize(scenePath))
        {
            SceneManager.Load(scene);
        }
        else
        {
            Log.Error("Sandbox: could not load start scene '{0}'.", scenePath);
        }
    }
}
