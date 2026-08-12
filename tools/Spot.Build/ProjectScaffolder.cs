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
        ValidateProjectName(name);

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

    // A project name doubles as its folder, its .csproj/.sln filename and (once spaces are stripped) the
    // generated C# namespace/assembly. Reject bad names up front with a clear message instead of letting
    // them surface later as a cryptic IO error or a namespace that won't compile.
    internal static void ValidateProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name must not be empty.", nameof(name));
        }

        int badPathChar = name.IndexOfAny(Path.GetInvalidFileNameChars());
        if (badPathChar >= 0)
        {
            throw new ArgumentException(
                $"Project name '{name}' contains an invalid character ('{name[badPathChar]}'). Use letters, digits, spaces or underscores.",
                nameof(name));
        }

        // The namespace is `name` with spaces removed (see ProjectGenerator.WriteProgram), so what's left
        // must be a valid C# identifier: it can't start with a digit and can only contain letters, digits
        // and underscores (Unicode letters/digits are fine — C# allows them in identifiers).
        string identifier = name.Replace(" ", string.Empty);
        if (identifier.Length == 0)
        {
            throw new ArgumentException("Project name must contain more than spaces.", nameof(name));
        }

        if (!char.IsLetter(identifier[0]) && identifier[0] != '_')
        {
            throw new ArgumentException(
                $"Project name '{name}' must start with a letter or underscore so it can be used as the project's namespace.",
                nameof(name));
        }

        foreach (char c in identifier)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                throw new ArgumentException(
                    $"Project name '{name}' can only contain letters, digits, spaces or underscores (it becomes the project's namespace).",
                    nameof(name));
            }
        }
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
