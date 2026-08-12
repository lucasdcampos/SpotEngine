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
        // what makes `spot run` behave like a real build rather than only working inside the editor.
        string contentRoot = Path.Combine(project.ProjectDirectory, ProjectStructure.ContentFolder);
        try
        {
            onOutput?.Invoke("Cooking assets...");
            var cook = Spot.Assets.AssetDatabase.CookAll(assetDir, contentRoot);
            if (cook.Failed > 0)
            {
                // A single bad asset shouldn't stop a test run, but make the gap loud.
                onError?.Invoke($"Warning: {cook.Failed} asset(s) failed to cook; those entries are missing. See the log for details.");
            }
            onOutput?.Invoke($"Cooked {cook.Cooked} asset(s).");
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Asset cook failed: {ex.Message}");
            return -1;
        }

        string csprojFile = project.Config.Name + ".csproj";
        string config = release ? "Release" : "Debug";

        // WorkingDirectory is the project folder so the launched game's current directory matches, letting
        // the engine find Content/manifest.json exactly where the cook wrote it. The child inherits our
        // console (no redirection) so game logs stream straight through, like `dotnet run` would.
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{csprojFile}\" -c {config}",
            WorkingDirectory = project.ProjectDirectory,
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
}
