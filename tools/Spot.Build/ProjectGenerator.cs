using System;
using System.IO;
using Spot.Core;

namespace Spot.Build;

/// <summary>
/// Generates the buildable IDE artifacts for a Spot project: copies the engine DLL into
/// <c>EngineBin/</c>, and writes <c>&lt;Name&gt;.csproj</c>, <c>&lt;Name&gt;.sln</c> and
/// <c>Program.cs</c>. Shared by the editor and the <c>spot</c> CLI so both stay in sync.
/// </summary>
public static class ProjectGenerator
{
    /// <summary>
    /// (Re)generates the build files for <paramref name="project"/>. <c>Program.cs</c> is only
    /// (re)written when <paramref name="overwriteProgram"/> is true or the file does not exist,
    /// so user edits to the entry point are preserved on a normal regenerate.
    /// </summary>
    public static void Generate(Project project, bool overwriteProgram = false)
    {
        if (string.IsNullOrEmpty(project.ProjectDirectory)) return;

        CopyEngineDll(project.ProjectDirectory);
        WriteCsproj(project);
        WriteSolution(project);
        WriteManifest(project, overwriteProgram);
        WriteProgram(project, overwriteProgram);
    }

    private static void CopyEngineDll(string projectDirectory)
    {
        string engineBinDir = Path.Combine(projectDirectory, Spot.Core.ProjectStructure.EngineBinFolder);
        Directory.CreateDirectory(engineBinDir);

        string sourceDllPath = typeof(Project).Assembly.Location;
        string engineDir = Path.GetDirectoryName(sourceDllPath) ?? string.Empty;

        // Always bundle the engine. Also bundle Spot.DebugUI when it ships beside the engine (the editor and
        // any host that references it), so the built game can host the in-runtime debug overlay; it is
        // optional, so a host without it simply produces a game without the overlay.
        CopyIfPresent(sourceDllPath, Path.Combine(engineBinDir, "Spot.Engine.dll"));
        CopyIfPresent(Path.Combine(engineDir, "Spot.DebugUI.dll"), Path.Combine(engineBinDir, "Spot.DebugUI.dll"));
    }

    private static void CopyIfPresent(string source, string target)
    {
        try
        {
            if (File.Exists(source))
            {
                File.Copy(source, target, overwrite: true);
            }
        }
        catch
        {
            // Ignore if the file is locked (e.g. the engine is running) or the copy fails.
        }
    }

    private static void WriteCsproj(Project project)
    {
        string csprojPath = Path.Combine(project.ProjectDirectory, project.Config.Name + ".csproj");

        string csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include=""Spot.Engine"">
      <HintPath>EngineBin\Spot.Engine.dll</HintPath>
    </Reference>
  </ItemGroup>

  <!-- Optional in-game debug overlay (hierarchy/inspector/time). Present when the editor/CLI bundled it into
       EngineBin; the engine hosts it automatically at runtime. Skipped cleanly when it was not bundled. -->
  <ItemGroup Condition=""Exists('EngineBin\Spot.DebugUI.dll')"">
    <Reference Include=""Spot.DebugUI"">
      <HintPath>EngineBin\Spot.DebugUI.dll</HintPath>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include=""Aether.Physics2D"" Version=""2.2.0"" />
    <PackageReference Include=""BepuPhysics"" Version=""2.4.0"" />
    <PackageReference Include=""Serilog"" Version=""4.4.0"" />
    <PackageReference Include=""Serilog.Sinks.Console"" Version=""6.1.1"" />
    <PackageReference Include=""Serilog.Sinks.File"" Version=""6.0.0"" />
    <PackageReference Include=""Silk.NET.Assimp"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.Input"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.OpenAL"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.OpenAL.Soft.Native"" Version=""1.23.1"" />
    <PackageReference Include=""Silk.NET.OpenGL"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.OpenGL.Extensions.ImGui"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.Windowing"" Version=""2.23.0"" />
    <PackageReference Include=""StbImageSharp"" Version=""2.30.15"" />
    <PackageReference Include=""StbTrueTypeSharp"" Version=""1.26.13"" />
    <PackageReference Include=""StbVorbisSharp"" Version=""1.22.4"" />
  </ItemGroup>

  <ItemGroup>
    <Compile Remove=""Build\**"" />
    <None Remove=""Build\**"" />
    <Content Remove=""Build\**"" />
    <EmbeddedResource Remove=""Build\**"" />
    <None Remove=""Assets\**"" />
    <Content Remove=""Assets\**"" />
    <EmbeddedResource Remove=""Assets\**"" />
  </ItemGroup>

  <ItemGroup>
    <!-- Ship cooked content, never the source Assets. The build step cooks Assets\ into Content\ first. -->
    <None Remove=""Content\**"" />
    <Content Include=""Content\**\*.*"" CopyToOutputDirectory=""PreserveNewest"" />
    <Content Include=""game.manifest"" CopyToOutputDirectory=""PreserveNewest"" />
  </ItemGroup>
</Project>";

        File.WriteAllText(csprojPath, csprojContent);
    }

    private static void WriteSolution(Project project)
    {
        string name = project.Config.Name;
        // Single, stable GUID reused across the project declaration and every configuration line.
        // (The previous editor implementation minted a fresh GUID per line, producing an .sln whose
        // ProjectConfigurationPlatforms entries referenced a project that did not exist.)
        string projectGuid = Guid.NewGuid().ToString().ToUpper();

        string slnContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""{name}"", ""{name}.csproj"", ""{{{projectGuid}}}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{{{projectGuid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{{{projectGuid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{{{projectGuid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{{{projectGuid}}}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
";

        File.WriteAllText(Path.Combine(project.ProjectDirectory, name + ".sln"), slnContent);
    }

    private static void WriteManifest(Project project, bool overwriteManifest)
    {
        string manifestPath = Path.Combine(project.ProjectDirectory, "game.manifest");
        if (!overwriteManifest && File.Exists(manifestPath)) return;

        var spec = new ApplicationSpec
        {
            Name = project.Config.Name,
            ContentDirectory = Spot.Core.ProjectStructure.ContentFolder,
            ManifestPath = "manifest.json",
            StartScene = project.Config.StartScene.Replace("\\", "/")
        };
        spec.Window.Title = project.Config.Name;
        spec.Window.Width = 1280;
        spec.Window.Height = 720;

        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(spec, options));
    }

    private static void WriteProgram(Project project, bool overwriteProgram)
    {
        string programPath = Path.Combine(project.ProjectDirectory, "Program.cs");
        if (!overwriteProgram && File.Exists(programPath)) return;

        string name = project.Config.Name;

        string programContent = $@"using System;
using Spot;
using Spot.Core;

namespace {name.Replace(" ", "")};

class Program
{{
    static void Main(string[] args)
    {{
        var app = SpotEngine.CreateApplication();
        app.Run();
    }}
}}
";
        File.WriteAllText(programPath, programContent);
    }
}
