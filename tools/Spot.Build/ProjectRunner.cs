using System;
using System.Diagnostics;
using System.IO;
using Spot.Core;

namespace Spot.Build;

/// <summary>
/// Builds and launches a Spot project from source with <c>dotnet run</c> — the quick-iteration path
/// that skips a full standalone publish. Like <see cref="ProjectBuilder"/> it regenerates the build
/// files and cooks assets first, then runs the game with its working directory set to the project
/// folder so the cooked <c>Content/</c> resolves exactly as a shipped build would.
/// </summary>
public static class ProjectRunner
{
    /// <summary>
    /// Regenerates build files, cooks assets, and runs the project, blocking until the game exits.
    /// Returns the game's exit code (or a negative value if it could not be started).
    /// </summary>
    /// <param name="project">The project to run.</param>
    /// <param name="release">Run a Release configuration instead of Debug.</param>
    /// <param name="onOutput">Receives progress lines from the regenerate/cook phase.</param>
    /// <param name="onError">Receives warnings/errors from the regenerate/cook phase.</param>
    public static int Run(Project project, bool release = false,
                          Action<string>? onOutput = null, Action<string>? onError = null)
    {
        if (string.IsNullOrEmpty(project.ProjectDirectory))
        {
            onError?.Invoke("Project has no directory on disk; cannot run.");
            return -1;
        }

        string assetDir = project.GetAssetDirectory();
        if (!Directory.Exists(assetDir))
        {
            onError?.Invoke($"No assets directory to cook: '{assetDir}'. Create it (or check the project's AssetDirectory) and try again.");
            return -1;
        }

        // Keep the .csproj and bundled engine DLL in sync with the current engine before running.
        ProjectGenerator.Generate(project);

        // The generated game loads cooked Content/, not source assets, so cook before launching — this is
        // what makes `spot run` behave like a real build rather than only working inside the editor. Cook into
        // a dedicated Build/run folder (with game.manifest beside it) and run from there, so the project root
        // stays clean and only Build/ is produced.
        string runDir = Path.Combine(project.ProjectDirectory, ProjectStructure.BuildFolder, "run");
        if (!ProjectBuilder.StageRuntimePayload(project, runDir, onOutput, onError))
        {
            return -1;
        }

        // Absolute so the project resolves regardless of the working directory we run the game from.
        string csprojFile = Path.Combine(project.ProjectDirectory, project.Config.Name + ".csproj");
        string config = release ? "Release" : "Debug";

        // WorkingDirectory is Build/run so the launched game's current directory holds the cooked
        // Content/manifest.json and game.manifest exactly as a shipped build would. The child inherits our
        // console (no redirection) so game logs stream straight through, like `dotnet run` would.
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{csprojFile}\" -c {config}",
            WorkingDirectory = runDir,
            UseShellExecute = false,
        };

        try
        {
            onOutput?.Invoke($"Running '{project.Config.Name}' ({config})...");
            using var process = new Process { StartInfo = processInfo };
            if (!process.Start())
            {
                onError?.Invoke("Failed to start the dotnet run process.");
                return -1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Failed to run project: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Cooks assets, (re)generates the browser WebAssembly project, stages cooked content into
    /// its <c>wwwroot/content</c>, and starts the WASM dev server with <c>dotnet run</c>.
    /// Blocks until the server exits (Ctrl+C). The SDK's built-in Kestrel server handles
    /// <c>application/wasm</c> correctly and prints the URL to the console.
    /// </summary>
    public static int RunBrowser(Project project,
                                 Action<string>? onOutput = null, Action<string>? onError = null)
    {
        if (string.IsNullOrEmpty(project.ProjectDirectory))
        {
            onError?.Invoke("Project has no directory on disk; cannot run.");
            return -1;
        }

        string assetDir = project.GetAssetDirectory();
        // Cook into a Build/ staging folder (not the project root) and copy it into wwwroot below.
        string contentRoot = Path.Combine(project.ProjectDirectory,
            ProjectStructure.BuildFolder, ProjectStructure.ContentFolder);
        try
        {
            onOutput?.Invoke("Cooking assets...");
            var cook = Spot.Assets.AssetDatabase.CookAll(assetDir, contentRoot);
            if (cook.Failed > 0)
            {
                onError?.Invoke($"Warning: {cook.Failed} asset(s) failed to cook; those entries are missing. See the log for details.");
            }
            onOutput?.Invoke($"Cooked {cook.Cooked} asset(s).");
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Asset cook failed: {ex.Message}");
            return -1;
        }

        string webDir = ProjectGenerator.GenerateBrowser(project);

        if (!File.Exists(Path.Combine(webDir, "EngineBin", "Spot.Engine.dll")))
        {
            onError?.Invoke(
                "The browser build of the engine (net10.0-browser Spot.Engine.dll) was not found. " +
                "Build the engine for the browser target first: dotnet build engine -f net10.0-browser");
            return -1;
        }

        try
        {
            string contentOut = Path.Combine(webDir, "wwwroot", "content");
            if (Directory.Exists(contentOut))
            {
                Directory.Delete(contentOut, recursive: true);
            }
            ProjectBuilder.StageContent(contentRoot, contentOut);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Failed to stage browser content: {ex.Message}");
            return -1;
        }

        string csprojFile = project.Config.Name + ".Browser.csproj";

        // Inherit the console (no redirection) so the server URL and logs stream straight through.
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{csprojFile}\"",
            WorkingDirectory = webDir,
            UseShellExecute = false,
        };

        try
        {
            onOutput?.Invoke($"Starting browser dev server for '{project.Config.Name}'...");
            onOutput?.Invoke("Press Ctrl+C to stop.");
            using var process = new Process { StartInfo = processInfo };
            if (!process.Start())
            {
                onError?.Invoke("Failed to start the dotnet run process.");
                return -1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Failed to run browser project: {ex.Message}");
            return -1;
        }
    }
}
