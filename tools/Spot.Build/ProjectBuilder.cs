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
                                    Action<string>? onOutput = null, Action<string>? onError = null)
    {
        if (string.IsNullOrEmpty(project.ProjectDirectory))
        {
            onError?.Invoke("Project has no directory on disk; cannot build.");
            return new BuildResult(false, -1, string.Empty);
        }

        // Keep the .csproj and bundled engine DLL in sync with the current engine before publishing.
        ProjectGenerator.Generate(project);

        string rid = RuntimeIdentifier(platform);
        string outputDir = Path.Combine(project.ProjectDirectory, "Build", FolderName(platform));
        string csprojFile = project.Config.Name + ".csproj";

        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{csprojFile}\" -c Release -r {rid} --self-contained true -p:PublishSingleFile=true -o \"{outputDir}\"",
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

            return new BuildResult(process.ExitCode == 0, process.ExitCode, outputDir);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Failed to build project: {ex.Message}");
            return new BuildResult(false, -1, outputDir);
        }
    }
}
