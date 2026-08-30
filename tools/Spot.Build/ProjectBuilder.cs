using System;
using System.Diagnostics;
using System.IO;
using Spot.Core;

namespace Spot.Build;

/// <summary>Target platform for a standalone build.</summary>
public enum BuildPlatform
{
    Windows,
    Linux,

    /// <summary>A WebAssembly build that runs the shared 3D pipeline in the browser on WebGL2 (no post-processing).</summary>
    Browser,
}

/// <summary>Outcome of a <see cref="ProjectBuilder.Build"/> call.</summary>
public readonly record struct BuildResult(bool Success, int ExitCode, string OutputDir);

/// <summary>
/// Publishes a Spot project into a self-contained, distributable build for a target platform.
/// Headless: streams the underlying <c>dotnet publish</c> output through callbacks and never opens
/// folders or writes to the console itself, so both the editor and the CLI can present results.
/// </summary>
public static class ProjectBuilder
{
    public static string RuntimeIdentifier(BuildPlatform platform) => platform switch
    {
        BuildPlatform.Windows => "win-x64",
        BuildPlatform.Linux => "linux-x64",
        BuildPlatform.Browser => "browser-wasm",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported build platform."),
    };

    private static string FolderName(BuildPlatform platform) => platform switch
    {
        BuildPlatform.Windows => "windows",
        BuildPlatform.Linux => "linux",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported build platform."),
    };

    /// <summary>
    /// Regenerates the build files, then runs <c>dotnet publish</c> for <paramref name="platform"/>
    /// as a self-contained build into <c>Build/&lt;platform&gt;</c> under the project directory.
    /// Blocks until the build finishes; callers that need to stay responsive should run this off the
    /// main thread.
    /// </summary>
    public static BuildResult Build(Project project, BuildPlatform platform,
                                    Action<string>? onOutput = null, Action<string>? onError = null,
                                    bool fastDebug = false)
    {
        if (string.IsNullOrEmpty(project.ProjectDirectory))
        {
            onError?.Invoke("Project has no directory on disk; cannot build.");
            return new BuildResult(false, -1, string.Empty);
        }

        if (platform == BuildPlatform.Browser)
        {
            return BuildBrowser(project, onOutput, onError);
        }

        // Keep the .csproj and bundled engine DLL in sync with the current engine before publishing.
        // Content and game.manifest are staged into the publish output *after* it succeeds (see below), so
        // the project root stays clean — a build leaves only Build/ behind.
        ProjectGenerator.Generate(project);

        string rid = RuntimeIdentifier(platform);

        // A distributable build goes to Build/<platform> as a self-contained, single-file Release.
        // The editor's Play uses fastDebug: a framework-dependent Debug build in Build/play that skips
        // the self-contained runtime copy and single-file bundling, cutting Play iteration time from
        // tens of seconds to a normal incremental build. It lives in its own folder so it never
        // clobbers a distributable build.
        string outputDir = Path.Combine(project.ProjectDirectory, Spot.Core.ProjectStructure.BuildFolder,
            fastDebug ? "play" : FolderName(platform));
        string csprojFile = project.Config.Name + ".csproj";

        string publishArgs = fastDebug
            ? $"publish \"{csprojFile}\" -c Debug -r {rid} --self-contained false -o \"{outputDir}\""
            : $"publish \"{csprojFile}\" -c Release -r {rid} --self-contained true -p:PublishSingleFile=true -o \"{outputDir}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = publishArgs,
            WorkingDirectory = project.ProjectDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = new Process { StartInfo = processInfo };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onOutput?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onError?.Invoke(e.Data); };

            if (!process.Start())
            {
                onError?.Invoke("Failed to start the dotnet publish process.");
                return new BuildResult(false, -1, outputDir);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return new BuildResult(false, process.ExitCode, outputDir);
            }

            // Publish succeeded: cook the assets straight into the output's Content/ and write game.manifest
            // beside the game, so it runs from its own folder without anything at the project root.
            if (!StageRuntimePayload(project, outputDir, onOutput, onError))
            {
                return new BuildResult(false, -1, outputDir);
            }

            return new BuildResult(true, 0, outputDir);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Failed to build project: {ex.Message}");
            return new BuildResult(false, -1, outputDir);
        }
    }

    /// <summary>
    /// Cooks the project's source assets into <c>&lt;outputDir&gt;/Content</c> and writes <c>game.manifest</c>
    /// into <paramref name="outputDir"/>, producing the runtime payload the published/launched game loads from
    /// its working directory. Kept out of the project root so a build or run leaves only <c>Build/</c> behind.
    /// Returns false (after reporting) when the cook throws.
    /// </summary>
    internal static bool StageRuntimePayload(Project project, string outputDir,
                                             Action<string>? onOutput, Action<string>? onError)
    {
        string contentRoot = Path.Combine(outputDir, Spot.Core.ProjectStructure.ContentFolder);
        try
        {
            onOutput?.Invoke("Cooking assets...");
            var cook = Spot.Assets.AssetDatabase.CookAll(project.GetAssetDirectory(), contentRoot);
            if (cook.Failed > 0)
            {
                // Don't abort (a single bad asset shouldn't block Play), but make the gap loud: the payload is
                // missing these entries and they will fail to load at runtime.
                onError?.Invoke($"Warning: {cook.Failed} asset(s) failed to cook; those entries are missing. See the log for details.");
            }
            onOutput?.Invoke($"Cooked {cook.Cooked} asset(s) -> {cook.ManifestPath}");

            ProjectGenerator.WriteManifest(project, outputDir);
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Asset cook failed: {ex.Message}");
            return false;
        }
    }

    // Cooks assets, generates the WebAssembly project, stages cooked content into its wwwroot with a preload
    // index, then publishes it. The published wwwroot is a static site: serve it with any host that returns
    // application/wasm for .wasm (the `dotnet serve`/`dotnet run` dev server does).
    private static BuildResult BuildBrowser(Project project, Action<string>? onOutput, Action<string>? onError)
    {
        // Cook into a Build/ staging folder (not the project root) and copy it into wwwroot below.
        string contentRoot = Path.Combine(project.ProjectDirectory,
            Spot.Core.ProjectStructure.BuildFolder, Spot.Core.ProjectStructure.ContentFolder);
        try
        {
            onOutput?.Invoke("Cooking assets...");
            var cook = Spot.Assets.AssetDatabase.CookAll(project.GetAssetDirectory(), contentRoot);
            if (cook.Failed > 0)
            {
                onError?.Invoke($"Warning: {cook.Failed} asset(s) failed to cook; the build is missing those entries.");
            }

            onOutput?.Invoke($"Cooked {cook.Cooked} asset(s) -> {cook.ManifestPath}");
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Asset cook failed: {ex.Message}");
            return new BuildResult(false, -1, contentRoot);
        }

        string webDir = ProjectGenerator.GenerateBrowser(project);
        if (!File.Exists(Path.Combine(webDir, "EngineBin", "Spot.Engine.dll")))
        {
            onError?.Invoke("The browser build of the engine (net10.0-browser Spot.Engine.dll) was not found. " +
                            "Build the engine for the browser target first (dotnet build engine -f net10.0-browser).");
            return new BuildResult(false, -1, webDir);
        }

        try
        {
            // Stage cooked content under wwwroot/content and write the preload index the host fetches first.
            string contentOut = Path.Combine(webDir, "wwwroot", "content");
            if (Directory.Exists(contentOut))
            {
                Directory.Delete(contentOut, recursive: true);
            }

            StageContent(contentRoot, contentOut);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Failed to stage browser content: {ex.Message}");
            return new BuildResult(false, -1, webDir);
        }

        // Absolute so `-o` is unambiguous: the publish runs with the WebAssembly project (webDir) as its
        // working directory, which is deeper than the project root, so a relative output path would nest.
        string outputDir = Path.GetFullPath(
            Path.Combine(project.ProjectDirectory, Spot.Core.ProjectStructure.BuildFolder, "browser"));
        string csprojFile = project.Config.Name + ".Browser.csproj";
        string publishArgs = $"publish \"{csprojFile}\" -c Release -o \"{outputDir}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = publishArgs,
            WorkingDirectory = webDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = new Process { StartInfo = processInfo };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onOutput?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onError?.Invoke(e.Data); };

            if (!process.Start())
            {
                onError?.Invoke("Failed to start the dotnet publish process.");
                return new BuildResult(false, -1, outputDir);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return new BuildResult(process.ExitCode == 0, process.ExitCode, outputDir);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Failed to build browser project: {ex.Message}");
            return new BuildResult(false, -1, outputDir);
        }
    }

    // Recursively copies cooked content to the browser output and writes content-index.txt: one line per file
    // (a forward-slash path relative to the content root) that the host fetches into its in-memory store.
    internal static void StageContent(string contentRoot, string contentOut)
    {
        Directory.CreateDirectory(contentOut);
        if (!Directory.Exists(contentRoot))
        {
            File.WriteAllText(Path.Combine(contentOut, "content-index.txt"), string.Empty);
            return;
        }

        var index = new System.Text.StringBuilder();
        foreach (string file in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(contentRoot, file).Replace('\\', '/');
            string destination = Path.Combine(contentOut, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            index.Append(relative).Append('\n');
        }

        File.WriteAllText(Path.Combine(contentOut, "content-index.txt"), index.ToString());
    }
}
