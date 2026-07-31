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
        WriteProgram(project, overwriteProgram);
    }

    private static void CopyEngineDll(string projectDirectory)
    {
        string engineBinDir = Path.Combine(projectDirectory, "EngineBin");
        Directory.CreateDirectory(engineBinDir);

        string sourceDllPath = typeof(Project).Assembly.Location;
        string targetDllPath = Path.Combine(engineBinDir, "Spot.Engine.dll");

        try
        {
            if (File.Exists(sourceDllPath))
            {
                File.Copy(sourceDllPath, targetDllPath, overwrite: true);
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

  <ItemGroup>
    <PackageReference Include=""Serilog"" Version=""4.4.0"" />
    <PackageReference Include=""Serilog.Sinks.Console"" Version=""6.1.1"" />
    <PackageReference Include=""Silk.NET.Assimp"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.Input"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.OpenGL"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.OpenGL.Extensions.ImGui"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.Windowing"" Version=""2.23.0"" />
    <PackageReference Include=""StbImageSharp"" Version=""2.30.15"" />
  </ItemGroup>

  <ItemGroup>
    <Compile Remove=""Build\**"" />
    <None Remove=""Build\**"" />
    <Content Remove=""Build\**"" />
    <EmbeddedResource Remove=""Build\**"" />
  </ItemGroup>

  <ItemGroup>
    <None Include=""Assets\**\*.*"" CopyToOutputDirectory=""PreserveNewest"" />
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

    private static void WriteProgram(Project project, bool overwriteProgram)
    {
        string programPath = Path.Combine(project.ProjectDirectory, "Program.cs");
        if (!overwriteProgram && File.Exists(programPath)) return;

        string name = project.Config.Name;
        string assetDir = project.Config.AssetDirectory.Replace("\\", "/");
        string startScene = project.Config.StartScene.Replace("\\", "/");

        string programContent = $@"using System;
using System.IO;
using Spot.Core;
using Spot.Scenes;
using Spot.Assets;

namespace {name.Replace(" ", "")};

class LoaderScene : Scene
{{
    public override void OnEnter()
    {{
        AssetPath.Root = ""{assetDir}"";
        string startScenePath = Path.Combine(AssetPath.Root, ""{startScene}"");
        if (File.Exists(startScenePath))
        {{
            var realScene = new Scene();
            var serializer = new SceneSerializer(realScene);
            serializer.Deserialize(startScenePath);
            SceneManager.Load(realScene);
        }}
        else
        {{
            Log.Error($""Start scene not found: {{startScenePath}}"");
        }}
    }}
}}

class Program
{{
    static void Main(string[] args)
    {{
        var spec = new ApplicationSpec
        {{
            Name = ""{name}""
        }};
        spec.Window.Title = ""{name}"";
        spec.Window.Width = 1280;
        spec.Window.Height = 720;

        var app = new Application(spec);
        app.Run(new LoaderScene());
    }}
}}
";
        File.WriteAllText(programPath, programContent);
    }
}
