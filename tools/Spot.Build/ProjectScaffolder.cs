using System.IO;
using Spot.Core;

namespace Spot.Build;

/// <summary>
/// Creates the on-disk structure for a brand-new Spot project (folder, <c>Assets/</c>, the
/// <c>.sptproj</c> config and an empty start scene) and generates its build files. Shared by the
/// launcher/editor "New Project" flow and the <c>spot new</c> CLI command.
/// </summary>
public static class ProjectScaffolder
{
    /// <summary>
    /// Creates a new project under <paramref name="location"/>/<paramref name="name"/>, sets it as
    /// <see cref="Project.Active"/>, and returns the path to the generated <c>.sptproj</c> file.
    /// </summary>
    public static string Create(string name, string location)
    {
        string projDir = Path.Combine(location, name);
        Directory.CreateDirectory(projDir);
        Directory.CreateDirectory(Path.Combine(projDir, "Assets"));

        string sptprojPath = Path.Combine(projDir, name + ".sptproj");

        Project.New();
        Project.Active!.Config.Name = name;
        Project.Active.Config.StartScene = "Scenes/Main.sptscene";
        Project.Active.ProjectDirectory = projDir;
        Project.SaveActive(sptprojPath); // Writes the .sptproj config JSON.

        WriteEmptyStartScene(Project.Active);

        ProjectGenerator.Generate(Project.Active); // Copies the engine DLL and writes .csproj/.sln/Program.cs.

        return sptprojPath;
    }

    // Writes a minimal empty scene so a freshly created project builds and runs (an empty window)
    // instead of logging "start scene not found". The JSON matches SceneSerializer's SceneData shape;
    // written directly to avoid coupling project authoring to the engine's graphics state.
    private static void WriteEmptyStartScene(Project project)
    {
        string scenePath = Path.Combine(project.GetAssetDirectory(), project.Config.StartScene);
        string? sceneDir = Path.GetDirectoryName(scenePath);
        if (!string.IsNullOrEmpty(sceneDir)) Directory.CreateDirectory(sceneDir);

        if (!File.Exists(scenePath))
        {
            File.WriteAllText(scenePath, "{\n  \"Entities\": []\n}\n");
        }
    }
}
