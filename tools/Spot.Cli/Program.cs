using System;
using System.Collections.Generic;
using System.IO;
using Spot.Build;
using Spot.Core;

namespace Spot.Cli;

// Alias inside the namespace body so it out-ranks the engine's parent Spot.Console namespace, which
// (being a member of the enclosing Spot namespace) would otherwise shadow System.Console.
using Console = System.Console;

/// <summary>
/// Command-line front-end for creating and building Spot projects. A thin wrapper over
/// <see cref="Spot.Build"/>: the same logic the editor uses in-process, exposed for headless
/// and standalone workflows. Never throws out of a command — errors are reported and mapped to a
/// non-zero exit code.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        // Engine paths (asset cook, migration) log through Serilog, which throws if never initialized.
        Log.Init();

        string command = args[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "new" => CmdNew(args),
                "generate" => CmdGenerate(args),
                "build" => CmdBuild(args),
                "cook" => CmdCook(args),
                "migrate" => CmdMigrate(args),
                "help" or "-h" or "--help" => PrintUsageAnd(0),
                _ => UnknownCommand(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    // spot new <name> [--path <dir>] [--build <windows|linux>]
    private static int CmdNew(string[] args)
    {
        var (positionals, options) = ParseArgs(args, 1);
        if (positionals.Count == 0)
        {
            Console.Error.WriteLine("error: 'new' requires a project name. Usage: spot new <name> [--path <dir>] [--build <windows|linux>]");
            return 1;
        }

        string name = positionals[0];
        string location = options.GetValueOrDefault("path") ?? Directory.GetCurrentDirectory();

        string sptproj = ProjectScaffolder.Create(name, Path.GetFullPath(location));
        Console.WriteLine($"Created project '{name}' at {Path.GetDirectoryName(sptproj)}");

        if (options.TryGetValue("build", out string? platformArg) && !string.IsNullOrEmpty(platformArg))
        {
            var project = Project.Load(sptproj) ?? throw new InvalidOperationException($"Failed to load the project just created: {sptproj}");
            return RunBuild(project, ParsePlatform(platformArg));
        }

        return 0;
    }

    // spot generate [--project <path>] [--full]
    private static int CmdGenerate(string[] args)
    {
        var (_, options) = ParseArgs(args, 1);
        var project = ResolveProject(options.GetValueOrDefault("project"));
        bool full = options.ContainsKey("full");

        ProjectGenerator.Generate(project, overwriteProgram: full);
        Console.WriteLine($"Generated build files for '{project.Config.Name}'{(full ? " (including Program.cs)" : "")}.");
        return 0;
    }

    // spot build <windows|linux> [--project <path>]
    private static int CmdBuild(string[] args)
    {
        var (positionals, options) = ParseArgs(args, 1);
        if (positionals.Count == 0)
        {
            Console.Error.WriteLine("error: 'build' requires a platform. Usage: spot build <windows|linux> [--project <path>]");
            return 1;
        }

        BuildPlatform platform = ParsePlatform(positionals[0]);
        var project = ResolveProject(options.GetValueOrDefault("project"));
        return RunBuild(project, platform);
    }

    // spot cook [--project <path>] [--out <dir>]
    private static int CmdCook(string[] args)
    {
        var (_, options) = ParseArgs(args, 1);
        var project = ResolveProject(options.GetValueOrDefault("project"));
        string outDir = options.GetValueOrDefault("out") ?? Path.Combine(project.ProjectDirectory, "Content");

        Console.WriteLine($"Cooking assets for '{project.Config.Name}' -> {outDir}");
        string manifest = Spot.Assets.AssetDatabase.CookAll(project.GetAssetDirectory(), outDir);
        Console.WriteLine($"Cooked. Manifest: {manifest}");
        return 0;
    }

    // spot migrate [--project <path>] [--dry-run]
    private static int CmdMigrate(string[] args)
    {
        var (_, options) = ParseArgs(args, 1);
        var project = ResolveProject(options.GetValueOrDefault("project"));
        bool dryRun = options.ContainsKey("dry-run");

        int changed = Spot.Assets.AssetDatabase.MigrateReferences(project.GetAssetDirectory(), dryRun);
        Console.WriteLine(dryRun
            ? $"{changed} file(s) would be migrated to guid references."
            : $"Migrated {changed} file(s) to guid references.");
        return 0;
    }

    private static int RunBuild(Project project, BuildPlatform platform)
    {
        Console.WriteLine($"Building '{project.Config.Name}' for {platform} ({ProjectBuilder.RuntimeIdentifier(platform)})...");
        var result = ProjectBuilder.Build(project, platform,
            onOutput: Console.WriteLine,
            onError: Console.Error.WriteLine);

        if (result.Success)
        {
            Console.WriteLine($"Build succeeded. Output: {result.OutputDir}");
            return 0;
        }

        Console.Error.WriteLine($"Build failed (exit code {result.ExitCode}).");
        return result.ExitCode == 0 ? 1 : result.ExitCode;
    }

    private static Project ResolveProject(string? projectOption)
    {
        string path = projectOption ?? Directory.GetCurrentDirectory();

        string sptproj;
        if (Directory.Exists(path))
        {
            string[] matches = Directory.GetFiles(path, "*.sptproj");
            if (matches.Length == 0) throw new FileNotFoundException($"No .sptproj found in '{path}'.");
            if (matches.Length > 1) throw new InvalidOperationException($"Multiple .sptproj files in '{path}'; specify one with --project.");
            sptproj = matches[0];
        }
        else if (File.Exists(path))
        {
            sptproj = path;
        }
        else
        {
            throw new FileNotFoundException($"Project path not found: '{path}'.");
        }

        return Project.Load(sptproj) ?? throw new InvalidOperationException($"Failed to load project: '{sptproj}'.");
    }

    private static BuildPlatform ParsePlatform(string value) => value.ToLowerInvariant() switch
    {
        "windows" or "win" => BuildPlatform.Windows,
        "linux" => BuildPlatform.Linux,
        _ => throw new ArgumentException($"Unknown platform '{value}'. Use 'windows' or 'linux'."),
    };

    // Splits args (from <start>) into positionals and options. An option is "--key"; it consumes the
    // following token as its value unless that token is itself an option (making it a boolean flag).
    private static (List<string> Positionals, Dictionary<string, string?> Options) ParseArgs(string[] args, int start)
    {
        var positionals = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (int i = start; i < args.Length; i++)
        {
            string token = args[i];
            if (token.StartsWith("--"))
            {
                string key = token[2..];
                string? value = null;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    value = args[++i];
                }
                options[key] = value;
            }
            else
            {
                positionals.Add(token);
            }
        }

        return (positionals, options);
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static int PrintUsageAnd(int code)
    {
        PrintUsage();
        return code;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"spot - Spot engine project tool

Usage:
  spot new <name> [--path <dir>] [--build <windows|linux>]
      Create a project (folder, Assets/, .sptproj) and generate its build files.
      With --build, also publish a standalone build for the given platform.

  spot generate [--project <path>] [--full]
      Regenerate .csproj/.sln and copy the engine DLL for an existing project.
      --full also overwrites Program.cs. <path> may be a .sptproj or a directory
      (defaults to the current directory).

  spot build <windows|linux> [--project <path>]
      Publish a self-contained standalone build for the platform into Build/<platform>.

  spot cook [--project <path>] [--out <dir>]
      Cook source assets into engine-native artifacts + a manifest (defaults to Content/).

  spot migrate [--project <path>] [--dry-run]
      Rewrite scene/material asset references to stable guid: references and generate
      missing .meta sidecars. --dry-run reports changes without writing.

  spot help
      Show this help.");
    }
}
