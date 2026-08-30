using System.IO;
using System.Text.Json;
using Spot.Assets;

namespace Spot.Core;

public class ProjectConfig
{
    public string Name { get; set; } = "New Project";
    public string StartScene { get; set; } = "Scenes/Main.sptscene";
    public string AssetDirectory { get; set; } = ProjectStructure.AssetsFolder;
}

public class Project
{
    public static Project? Active { get; private set; }

    public ProjectConfig Config { get; private set; }
    public string ProjectDirectory { get; set; }
    public string FilePath { get; private set; }

    private Project(ProjectConfig config, string directory, string filepath)
    {
        Config = config;
        ProjectDirectory = directory;
        FilePath = filepath;
    }

    public static Project New()
    {
        Active = new Project(new ProjectConfig(), string.Empty, string.Empty);
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
                Active = new Project(config, Path.GetDirectoryName(filepath) ?? string.Empty, filepath);
                AssetPath.Root = Active.GetAssetDirectory();
                return Active;
            }
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to load project '{0}': {1}", filepath, ex.Message);
        }

        return null;
    }

    // Writes the active project's config to disk. Generating the buildable IDE artifacts
    // (.csproj/.sln/Program.cs and the EngineBin DLL) is handled separately by Spot.Build so this
    // stays usable by the runtime without pulling in the authoring/build tooling.
    public static void SaveActive(string filepath)
    {
        if (Active == null) return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(Active.Config, options);
        File.WriteAllText(filepath, json);
        Active.ProjectDirectory = Path.GetDirectoryName(filepath) ?? string.Empty;
        Active.FilePath = filepath;
        AssetPath.Root = Active.GetAssetDirectory();
    }

    public string GetAssetDirectory()
    {
        if (string.IsNullOrEmpty(ProjectDirectory)) return Config.AssetDirectory;
        return Path.Combine(ProjectDirectory, Config.AssetDirectory);
    }
}
