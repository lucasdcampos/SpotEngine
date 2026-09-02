using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Spot.Core;

namespace Spot.Editor.Utils;

/// <summary>
/// The editor-camera pose saved for one open scene tab, keyed by the scene's file path, so reopening
/// a project restores each viewport to where the user left it (position, orientation, 2D zoom, mode).
/// </summary>
public sealed class SceneCameraState
{
    public string Path { get; set; } = string.Empty;
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float Zoom { get; set; } = 2.0f;
    public bool Is3D { get; set; } = true;
}

/// <summary>
/// The working-session snapshot the editor restores on reopen: which scenes / UI documents / animator
/// controllers were open (and which was active), the per-scene camera pose, and the panel visibility.
/// </summary>
public sealed class EditorSessionState
{
    public List<string> OpenScenes { get; set; } = new();
    public string? ActiveScene { get; set; }
    public List<string> OpenUIDocuments { get; set; } = new();
    public string? ActiveUIDocument { get; set; }
    public List<string> OpenAnimators { get; set; } = new();
    public List<SceneCameraState> Cameras { get; set; } = new();

    // Panel visibility (View > Panels).
    public bool ShowGame { get; set; } = true;
    public bool ShowHierarchy { get; set; } = true;
    public bool ShowInspector { get; set; } = true;
    public bool ShowConsole { get; set; } = true;
    public bool ShowAssetBrowser { get; set; } = true;
    public bool ShowProjectSettings { get; set; }
}

/// <summary>
/// Persists the editor's working session <b>per project</b> so reopening a project continues from where
/// the user left off. Stored under the project's <c>Library</c> folder (editor-only, typically out of
/// version control), next to the other on-demand editor caches. All I/O is best-effort: a missing or
/// corrupt file simply yields no restore, never an exception.
/// </summary>
public static class EditorSession
{
    private const string FileName = "editor_session.json";

    private static string PathFor(Project project) =>
        Path.Combine(project.ProjectDirectory, ProjectStructure.LibraryFolder, FileName);

    /// <summary>Reads the saved session for a project, or <c>null</c> when there is none (or it is unreadable).</summary>
    public static EditorSessionState? Load(Project project)
    {
        try
        {
            string path = PathFor(project);
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<EditorSessionState>(File.ReadAllText(path));
            }
        }
        catch { /* ignore corrupt/unreadable session */ }
        return null;
    }

    /// <summary>Writes the current session for a project, creating the <c>Library</c> folder if needed.</summary>
    public static void Save(Project project, EditorSessionState state)
    {
        try
        {
            string path = PathFor(project);
            string? dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort persistence */ }
    }
}
