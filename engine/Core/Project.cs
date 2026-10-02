using System.IO;
using System.Text.Json;
using Spot.Engine.Assets;
using Spot.Framework;
using Spot.Framework.Audio;

namespace Spot.Engine;

public class ProjectConfig
{
    public string Name { get; set; } = "New Project";
    public string StartScene { get; set; } = "Scenes/Main.sptscene";
    public string AssetDirectory { get; set; } = ProjectStructure.AssetsFolder;

    /// <summary>
    /// The project's audio mixer layout — the volume groups (Music, SFX, UI …) audio sources route to. Authored
    /// in the editor's Audio Mixer panel, applied to <see cref="AudioMixer"/> when the project loads, and copied
    /// into a build's <c>game.manifest</c> so a shipped game boots with the same mix. Empty means the engine
    /// defaults.
    /// </summary>
    public List<AudioBusDefinition> AudioBuses { get; set; } = new();
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
                AudioMixer.SetLayout(config.AudioBuses);
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

        // The mixer is the live source of truth for the bus layout while the editor runs, so it is captured
        // here rather than mirrored into the config on every fader move.
        Active.Config.AudioBuses = AudioMixer.GetLayout();

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
