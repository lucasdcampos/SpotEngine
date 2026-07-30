using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Scenes;
using Spot.Editor.UI;
using Spot.Editor.Utils;

namespace Spot.Editor;

/// <summary>
/// The project launcher shown before the editor. Lets the user create a project, reopen a recent
/// one, or browse for an existing project, then switches to the editor loaded with that project.
/// </summary>
public class LauncherScene : Scene
{
    private List<string> _recent = new();

    private bool _isCreatingProject;
    private string _newProjectName = "MyProject";
    private string _newProjectLocation =
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
    private string? _error;

    // Deferred so the recent list is not mutated while it is being iterated.
    private string? _pendingOpen;
    private string? _pendingRemove;

    public override void OnEnter()
    {
        EditorThemeManager.SetTheme(EditorThemes.SpotDark);
        Application.Instance.Window.NativeWindow.Title = "Spot Launcher";
        _recent = RecentProjects.Load();
    }

    public override void OnImGuiRender()
    {
        _pendingOpen = null;
        _pendingRemove = null;

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.WorkPos);
        ImGui.SetNextWindowSize(vp.WorkSize);
        ImGui.Begin("##Launcher",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus);

        DrawHeader();
        DrawActions();
        ImGui.Spacing();
        DrawRecents();

        if (_error != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(EditorThemeManager.Current.Palette.LogError, _error);
        }

        DrawNewProjectModal();

        ImGui.End();

        // Apply deferred actions after the UI for this frame is complete.
        if (_pendingRemove != null)
        {
            RecentProjects.Remove(_pendingRemove);
            _recent = RecentProjects.Load();
        }
        if (_pendingOpen != null)
        {
            OpenProject(_pendingOpen);
        }
    }

    private void DrawHeader()
    {
        ImGui.SetWindowFontScale(1.8f);
        ImGui.TextUnformatted("Spot Engine");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.TextDisabled("Project Launcher");
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawActions()
    {
        var buttonSize = new Vector2(170, 40);
        if (ImGui.Button("New Project", buttonSize))
        {
            _isCreatingProject = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Open Project...", buttonSize))
        {
            string? path = FileDialogs.OpenFile("Spot Project (*.sptproj)|*.sptproj");
            if (path != null)
            {
                _pendingOpen = path;
            }
        }
    }

    private void DrawRecents()
    {
        ImGui.TextUnformatted("Recent Projects");
        ImGui.Separator();

        ImGui.BeginChild("##recents");
        if (_recent.Count == 0)
        {
            ImGui.TextDisabled("No recent projects yet. Create one or open an existing project.");
        }
        else
        {
            var palette = EditorThemeManager.Current.Palette;
            float rowHeight = ImGui.GetTextLineHeight() * 2 + 12;

            foreach (var path in _recent)
            {
                ImGui.PushID(path);
                Vector2 p0 = ImGui.GetCursorScreenPos();
                if (ImGui.Selectable("##row", false, ImGuiSelectableFlags.None, new Vector2(0, rowHeight)))
                {
                    _pendingOpen = path;
                }

                if (ImGui.BeginPopupContextItem("rowctx"))
                {
                    if (ImGui.MenuItem("Open")) _pendingOpen = path;
                    if (ImGui.MenuItem("Open Containing Folder"))
                    {
                        string? dir = Path.GetDirectoryName(path);
                        if (dir != null) OpenExternally(dir);
                    }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Remove from Recents")) _pendingRemove = path;
                    ImGui.EndPopup();
                }

                var drawList = ImGui.GetWindowDrawList();
                drawList.AddText(p0 + new Vector2(10, 6), ImGui.GetColorU32(palette.Text),
                    Path.GetFileNameWithoutExtension(path));
                drawList.AddText(p0 + new Vector2(10, 6 + ImGui.GetTextLineHeight() + 2),
                    ImGui.GetColorU32(palette.TextDisabled), Path.GetDirectoryName(path) ?? path);

                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }

    private void DrawNewProjectModal()
    {
        if (_isCreatingProject)
        {
            ImGui.OpenPopup("Create New Project");
        }

        bool open = true;
        if (ImGui.BeginPopupModal("Create New Project", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("Project Name", ref _newProjectName, 128);
            ImGui.InputText("Location", ref _newProjectLocation, 256);
            ImGui.SameLine();
            if (ImGui.Button("...##Location"))
            {
                string? folder = FileDialogs.SelectFolder();
                if (folder != null) _newProjectLocation = folder;
            }

            ImGui.Spacing();
            bool canCreate = !string.IsNullOrWhiteSpace(_newProjectName) && !string.IsNullOrWhiteSpace(_newProjectLocation);
            if (!canCreate) ImGui.BeginDisabled();
            if (ImGui.Button("Create", new Vector2(120, 0)))
            {
                CreateProject(_newProjectName.Trim(), _newProjectLocation);
                _isCreatingProject = false;
                ImGui.CloseCurrentPopup();
            }
            if (!canCreate) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                _isCreatingProject = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        else if (!open)
        {
            _isCreatingProject = false;
        }
    }

    private void CreateProject(string name, string location)
    {
        try
        {
            string sptproj = ProjectFactory.Create(name, location);
            RecentProjects.Add(sptproj);
            OpenEditor();
        }
        catch (System.Exception ex)
        {
            _error = $"Could not create project: {ex.Message}";
        }
    }

    private void OpenProject(string sptprojPath)
    {
        if (Project.Load(sptprojPath) != null)
        {
            RecentProjects.Add(sptprojPath);
            OpenEditor();
        }
        else
        {
            _error = $"Could not open project: {sptprojPath}";
            RecentProjects.Remove(sptprojPath);
            _recent = RecentProjects.Load();
        }
    }

    // Switches to the editor loaded with the now-active project (applied at the next frame boundary).
    private static void OpenEditor() => SceneManager.Load(new EditorScene());

    private static void OpenExternally(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
