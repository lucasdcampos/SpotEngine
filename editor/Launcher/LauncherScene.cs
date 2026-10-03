using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Spot.Build;
using Spot.DebugUI.UI;
using Spot.Editor.UI;
using Spot.Editor.Utils;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.Editor.Launcher;

/// <summary>
/// The project launcher shown before the editor: create a project, reopen a recent one, or browse for an
/// existing one, then switch to the editor loaded with it. The scene is split across partial files by region:
/// this file holds the frame flow, keyboard shortcuts and the loading hand-off; <c>.Sidebar</c> the brand and
/// primary actions; <c>.Projects</c> the header and project cards; <c>.NewProject</c> the creation dialog.
/// </summary>
public partial class LauncherScene : Scene
{
    /// <summary>The launcher's window size. Compact, centered; the editor restores its own size when it loads.</summary>
    public const int WindowWidth = 960;
    public const int WindowHeight = 600;

    private const string RepoUrl = "https://github.com/lucasdcampos/spotengine";
    private const string DocsUrl = RepoUrl + "/tree/master/docs";

    // Cover frames to present before running the heavy open/create work. Enough for the editor-sized
    // window (requested when loading begins) to finish resizing so the freeze that follows happens
    // under a full-size loading screen instead of the old, small one.
    private const int LoadingSettleFrames = 2;

    private readonly ProjectThumbnailCache _thumbnails = new();
    private LauncherPreferences _preferences = new();
    private List<RecentProject> _recent = new();
    private string? _error;

    // Actions requested while drawing, applied once the frame's UI is complete so the recent list is never
    // mutated mid-iteration and native dialogs never open inside a half-built window.
    private string? _pendingOpen;
    private string? _pendingRemove;
    private bool _pendingBrowse;

    // Loading hand-off: once a project is chosen we show a loading cover for a frame (so it is
    // actually presented) before running the heavy open/create work, which then switches to the
    // editor. This keeps a real "Loading..." screen on-screen during the ~2s freeze instead of a
    // frozen launcher.
    private bool _loading;
    private int _loadingFrames;
    private string _loadingTitle = "";
    private string _loadingSubtitle = "";
    private Action? _loadWork;

    public override void OnEnter()
    {
        EditorThemeManager.SetTheme(EditorThemes.SpotDark);

        var native = Application.Instance.Window.NativeWindow;
        native.Title = $"Spot {Application.Instance.EngineVersion}";

        // Shrink the window down to the launcher's compact size and center it on the monitor. The
        // editor restores its own (larger) size when it loads.
        try
        {
            native.WindowState = WindowState.Normal;
            native.Size = new Vector2D<int>(WindowWidth, WindowHeight);
            native.Center();
        }
        catch { /* window sizing is best-effort; never let it take the launcher down */ }

        _recent = RecentProjects.Load();
        _preferences = LauncherPreferences.Load();
    }

    public override void OnExit()
    {
        _thumbnails.Dispose();
    }

    public override void OnImGuiRender()
    {
        var palette = EditorThemeManager.Current.Palette;

        // Loading state: draw the cover, then (once it has been presented at least once) run the
        // pending open/create work. Nothing else in the launcher is drawn.
        if (_loading)
        {
            LoadingScreen.Present(palette, _loadingTitle, _loadingSubtitle);

            if (_loadingFrames >= LoadingSettleFrames && _loadWork != null)
            {
                var work = _loadWork;
                _loadWork = null;
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    // Never strand the launcher on the loading screen: fall back to the launcher UI.
                    _error = $"Could not open project: {ex.Message}";
                    _loading = false;
                }
            }
            _loadingFrames++;
            return;
        }

        var colors = LauncherColors.From(palette);
        _thumbnails.Update();
        List<RecentProject> visible = VisibleProjects();
        HandleShortcuts(visible);

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.Pos);
        ImGui.SetNextWindowSize(vp.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, colors.Main);
        ImGui.Begin("##Launcher",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);

        DrawSidebar(colors);
        ImGui.SameLine(0, 0);
        DrawMain(colors, visible);
        DrawNewProjectModal(colors);

        ImGui.End();

        ApplyPendingActions();
    }

    // ----- Project list ------------------------------------------------------------------------------------

    // The recent projects matching the search, in the chosen order.
    private List<RecentProject> VisibleProjects()
    {
        IEnumerable<RecentProject> projects = _recent;
        string query = _search.Trim();
        if (query.Length > 0)
        {
            projects = projects.Where(p =>
                ProjectName(p).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        projects = _preferences.Sort == ProjectSort.Name
            ? projects.OrderBy(ProjectName, StringComparer.CurrentCultureIgnoreCase)
            : projects.OrderByDescending(p => p.LastOpenedUtc ?? DateTime.MinValue); // stable: ties keep list order

        return projects.ToList();
    }

    private static string ProjectName(RecentProject project) => Path.GetFileNameWithoutExtension(project.Path);

    private static string ProjectDirectory(RecentProject project) =>
        Path.GetDirectoryName(project.Path) ?? project.Path;

    // ----- Keyboard ----------------------------------------------------------------------------------------

    // Ctrl+N new, Ctrl+O open, Ctrl+F search; with no text field focused, arrows/Home/End move the selection,
    // Enter opens it, Delete removes it from the list, Ctrl+C copies its path, and Esc clears the search, then
    // the selection. Nothing fires while a dialog or menu is open — those handle their own keys.
    private void HandleShortcuts(List<RecentProject> visible)
    {
        if (ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel)) return;

        var io = ImGui.GetIO();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.N, false)) { OpenNewProjectDialog(); return; }
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.O, false)) { _pendingBrowse = true; return; }
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.F, false)) { _focusSearch = true; return; }
        if (io.WantTextInput) return;

        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            if (_search.Length > 0) _search = string.Empty;
            else _selectedPath = null;
            return;
        }
        if (visible.Count == 0) return;

        int index = visible.FindIndex(p => p.Path == _selectedPath);
        int columns = _preferences.View == ProjectView.Grid ? _columns : 1;
        int? target = null;
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) target = index < 0 ? 0 : index + columns;
        else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) target = index < 0 ? 0 : index - columns;
        else if (columns > 1 && ImGui.IsKeyPressed(ImGuiKey.RightArrow)) target = index + 1;
        else if (columns > 1 && ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) target = index < 0 ? 0 : index - 1;
        else if (ImGui.IsKeyPressed(ImGuiKey.Home, false)) target = 0;
        else if (ImGui.IsKeyPressed(ImGuiKey.End, false)) target = visible.Count - 1;

        if (target != null)
        {
            Select(visible[Math.Clamp(target.Value, 0, visible.Count - 1)].Path, scrollIntoView: true);
            return;
        }
        if (index < 0) return;

        string selected = visible[index].Path;
        if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false)) _pendingOpen = selected;
        else if (ImGui.IsKeyPressed(ImGuiKey.Delete, false)) _pendingRemove = selected;
        else if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C, false)) ImGui.SetClipboardText(selected);
    }

    private void Select(string path, bool scrollIntoView = false)
    {
        _selectedPath = path;
        _scrollToSelection = scrollIntoView;
    }

    // ----- Deferred actions --------------------------------------------------------------------------------

    private void ApplyPendingActions()
    {
        if (_pendingRemove != null)
        {
            // Keep the selection on the same slot so repeated Delete presses walk down the list.
            List<RecentProject> before = VisibleProjects();
            int slot = before.FindIndex(p => p.Path == _pendingRemove);

            RecentProjects.Remove(_pendingRemove);
            _recent = RecentProjects.Load();

            if (_selectedPath == _pendingRemove)
            {
                List<RecentProject> after = VisibleProjects();
                _selectedPath = after.Count > 0 ? after[Math.Clamp(slot, 0, after.Count - 1)].Path : null;
            }
            _pendingRemove = null;
        }

        if (_pendingBrowse)
        {
            _pendingBrowse = false;
            string? path = FileDialogs.OpenFile("Spot Project (*.sptproj)|*.sptproj");
            if (path != null) _pendingOpen = path;
        }

        if (_pendingOpen != null)
        {
            string path = _pendingOpen;
            _pendingOpen = null;
            StartOpen(path);
        }
    }

    // ----- Loading hand-off --------------------------------------------------------------------------------

    // Begins the loading cover, deferring the heavy open work until the cover has been presented.
    private void StartOpen(string sptprojPath)
    {
        BeginLoading(Path.GetFileNameWithoutExtension(sptprojPath), "Opening project...", () =>
        {
            if (Project.Load(sptprojPath) != null)
            {
                RecentProjects.Add(sptprojPath, Application.Instance.EngineVersion);
                OpenEditor();
            }
            else
            {
                _error = $"Could not open project: {sptprojPath}";
                RecentProjects.Remove(sptprojPath);
                _recent = RecentProjects.Load();
                _loading = false;
            }
        });
    }

    private void StartCreate(string name, string location)
    {
        BeginLoading(name, "Creating project...", () =>
        {
            try
            {
                string sptproj = ProjectScaffolder.Create(name, location);
                RecentProjects.Add(sptproj, Application.Instance.EngineVersion);
                OpenEditor();
            }
            catch (Exception ex)
            {
                _error = $"Could not create project: {ex.Message}";
                _loading = false;
            }
        });
    }

    private void BeginLoading(string title, string subtitle, Action work)
    {
        _error = null;
        _loading = true;
        _loadingFrames = 0;
        _loadingTitle = title;
        _loadingSubtitle = subtitle;
        _loadWork = work;

        // Resize the window to the editor's final size now, while the loading cover is still being
        // redrawn every frame, so it settles before the ~2s freeze in the editor's OnEnter (which
        // would otherwise leave the small launcher frame frozen in the corner of a maximized window).
        try
        {
            EditorSettings.PrepareEditorWindow(Application.Instance.Window.NativeWindow);
        }
        catch { /* window sizing is best-effort */ }
    }

    // Switches to the editor loaded with the now-active project (applied at the next frame boundary).
    private static void OpenEditor() => SceneManager.Load(new EditorScene());

    // ----- Shell helpers -----------------------------------------------------------------------------------

    private static void OpenExternally(string pathOrUrl)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = pathOrUrl,
                UseShellExecute = true
            });
        }
        catch { /* nothing to open it with; not worth interrupting the user */ }
    }

    /// <summary>The platform's name for showing a file in its folder.</summary>
    private static string RevealLabel =>
        OperatingSystem.IsWindows() ? "Show in Explorer" :
        OperatingSystem.IsMacOS() ? "Reveal in Finder" : "Open Containing Folder";

    // Opens the file manager on the project's folder with the .sptproj selected where the platform supports it.
    private static void RevealInFileManager(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                return;
            }
            if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", new[] { "-R", filePath });
                return;
            }
        }
        catch { /* fall back to opening the folder */ }

        string? dir = Path.GetDirectoryName(filePath);
        if (dir != null) OpenExternally(dir);
    }
}
