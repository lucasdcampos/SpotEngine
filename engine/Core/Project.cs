using System.IO;
using System.Text.Json;

namespace Spot.Core;

public class ProjectConfig
{
    public string Name { get; set; } = "New Project";
    public string StartScene { get; set; } = "Scenes/Main.spotscene";
    public string AssetDirectory { get; set; } = "Assets";
}

public class Project
{
    public static Project? Active { get; private set; }

    public ProjectConfig Config { get; private set; }
    public string ProjectDirectory { get; private set; }

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

    private static void GenerateCSProject()
    {
        if (Active == null || string.IsNullOrEmpty(Active.ProjectDirectory)) return;

        string csprojPath = Path.Combine(Active.ProjectDirectory, Active.Config.Name + ".csproj");
        string engineDllPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Spot.Engine.dll"));
        
        string csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include=""Spot.Engine"">
      <HintPath>{engineDllPath}</HintPath>
    </Reference>
  </ItemGroup>
</Project>";
        
        File.WriteAllText(csprojPath, csprojContent);
    }

    public string GetAssetDirectory()
    {
        if (string.IsNullOrEmpty(ProjectDirectory)) return Config.AssetDirectory;
        return Path.Combine(ProjectDirectory, Config.AssetDirectory);
    }
}
