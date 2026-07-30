using System.IO;
using System.Text.Json;

namespace Spot.Core;

public class ProjectConfig
{
    public string Name { get; set; } = "New Project";
    public string StartScene { get; set; } = "Scenes/Main.sptscene";
    public string AssetDirectory { get; set; } = "Assets";
}

public class Project
{
    public static Project? Active { get; private set; }

    public ProjectConfig Config { get; private set; }
    public string ProjectDirectory { get; set; }

    private Project(ProjectConfig config, string directory)
    {
        Config = config;
        ProjectDirectory = directory;
    }

    public static Project New()
    {
        Active = new Project(new ProjectConfig(), string.Empty);
        return Active;
    }

    public static Project? Load(string filepath)
    {
        if (!File.Exists(filepath)) return null;

        try
        {
            string json = File.ReadAllText(filepath);
            var config = JsonSerializer.Deserialize<ProjectConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (config != null)
            {
                Active = new Project(config, Path.GetDirectoryName(filepath) ?? string.Empty);
                return Active;
            }
        }
        catch
        {
            // Log error
        }
        
        return null;
    }

    public static void SaveActive(string filepath)
    {
        if (Active == null) return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(Active.Config, options);
        File.WriteAllText(filepath, json);
        Active.ProjectDirectory = Path.GetDirectoryName(filepath) ?? string.Empty;
        
        GenerateCSProject();
    }

    public static void GenerateCSProject(bool overwriteProgram = false)
    {
        if (Active == null || string.IsNullOrEmpty(Active.ProjectDirectory)) return;

        string engineBinDir = Path.Combine(Active.ProjectDirectory, "EngineBin");
        if (!Directory.Exists(engineBinDir))
        {
            Directory.CreateDirectory(engineBinDir);
        }

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
            // Ignore if file is locked or fails
        }

        string csprojPath = Path.Combine(Active.ProjectDirectory, Active.Config.Name + ".csproj");

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
    <PackageReference Include=""Silk.NET.Input"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.OpenGL"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.OpenGL.Extensions.ImGui"" Version=""2.23.0"" />
    <PackageReference Include=""Silk.NET.Windowing"" Version=""2.23.0"" />
    <PackageReference Include=""StbImageSharp"" Version=""2.30.15"" />
  </ItemGroup>

  <ItemGroup>
    <Compile Remove=""Build\**"" />
    <None Remove=""Build\**"" />
    <EmbeddedResource Remove=""Build\**"" />
  </ItemGroup>

  <ItemGroup>
    <None Include=""**\*.sptproj"" CopyToOutputDirectory=""PreserveNewest"" Exclude=""Build\**;bin\**;obj\**"" />
    <None Include=""Assets\**\*.*"" CopyToOutputDirectory=""PreserveNewest"" />
  </ItemGroup>
</Project>";
        
        File.WriteAllText(csprojPath, csprojContent);

        string programPath = Path.Combine(Active.ProjectDirectory, "Program.cs");
        if (overwriteProgram || !File.Exists(programPath))
        {
            string programContent = $@"using System;
using System.IO;
using Spot.Core;
using Spot.Scenes;

namespace {Active.Config.Name.Replace(" ", "")};

class LoaderScene : Scene
{{
    public override void OnEnter()
    {{
        string projectPath = ""{Active.Config.Name}.sptproj"";
        if (Project.Load(projectPath) != null && Project.Active != null)
        {{
            string startScenePath = Path.Combine(Project.Active.GetAssetDirectory(), Project.Active.Config.StartScene);
            if (File.Exists(startScenePath))
            {{
                var realScene = new Scene();
                var serializer = new SceneSerializer(realScene);
                serializer.Deserialize(startScenePath);
                SceneManager.Load(realScene);
            }}
            else
            {{
                Console.WriteLine($""Start scene not found: {{startScenePath}}"");
            }}
        }}
        else
        {{
            Console.WriteLine(""Warning: Could not load project configuration."");
        }}
    }}
}}

class Program
{{
    static void Main(string[] args)
    {{
        var spec = new ApplicationSpec
        {{
            Name = ""{Active.Config.Name}""
        }};
        spec.Window.Title = ""{Active.Config.Name}"";
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

    public string GetAssetDirectory()
    {
        if (string.IsNullOrEmpty(ProjectDirectory)) return Config.AssetDirectory;
        return Path.Combine(ProjectDirectory, Config.AssetDirectory);
    }
}
