using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ImGuiNET;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Spot.Core;
using Spot.Build;
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
    // A compact, centered window: the launcher does not need the editor's full working area.
    private const int LauncherWidth = 1000;
    private const int LauncherHeight = 620;
    private const float SidebarWidth = 300.0f;

    // Cover frames to present before running the heavy open/create work. Enough for the editor-sized
    // window (requested when loading begins) to finish resizing so the freeze that follows happens
    // under a full-size loading screen instead of the old, small one.
    private const int LoadingSettleFrames = 2;

    private List<string> _recent = new();

    private bool _isCreatingProject;
    private string _newProjectName = "MyProject";
    private string _newProjectLocation =
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
    private string? _error;

    // Deferred so the recent list is not mutated while it is being iterated.
    private string? _pendingOpen;
    private string? _pendingRemove;

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
        native.Title = "Spot Launcher";

        // Shrink the window down to the launcher's compact size and center it on the monitor. The
        // editor restores its own (larger) size when it loads.
        try
        {
            native.WindowState = WindowState.Normal;
            native.Size = new Vector2D<int>(LauncherWidth, LauncherHeight);
            native.Center();
        }
        catch { /* window sizing is best-effort; never let it take the launcher down */ }

        _recent = RecentProjects.Load();
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
                catch (System.Exception ex)
                {
                    // Never strand the launcher on the loading screen: fall back to the launcher UI.
                    _error = $"Could not open project: {ex.Message}";
                    _loading = false;
                }
            }
            _loadingFrames++;
            return;
        }

        _pendingOpen = null;
        _pendingRemove = null;

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.Pos);
        ImGui.SetNextWindowSize(vp.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("##Launcher",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.PopStyleVar();

        DrawSidebar(palette);
        ImGui.SameLine(0, 0);
        DrawRecentsPanel(palette);

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
            StartOpen(_pendingOpen);
        }
    }

    // ----- Left sidebar: branding + primary actions -----------------------------------------------

    private void DrawSidebar(EditorPalette palette)
    {
        // AlwaysUseWindowPadding is required: borderless child windows ignore WindowPadding by
        // default, so without it the content would sit flush against the child's edge. Pop the var
        // right after BeginChild (the child captures it there) so it does not leak into nested childs.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Darken(palette.WindowBg, 0.35f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(26, 28));
        ImGui.BeginChild("##sidebar", new Vector2(SidebarWidth, 0), ImGuiChildFlags.AlwaysUseWindowPadding);
        ImGui.PopStyleVar();

        DrawLogo(palette);

        ImGui.Dummy(new Vector2(0, 16));
        ImGui.SetWindowFontScale(1.6f);
        ImGui.TextUnformatted("Spot Engine");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.TextDisabled($"Game Engine  ·  v{Application.Instance.EngineVersion}");

        ImGui.Dummy(new Vector2(0, 28));
        DrawSectionLabel(palette, "START");
        ImGui.Dummy(new Vector2(0, 6));

        float w = ImGui.GetContentRegionAvail().X;
        if (ActionButton(palette, "##new", "New Project", new Vector2(w, 44), filled: true, Icon.Plus))
        {
            _isCreatingProject = true;
        }
        ImGui.Dummy(new Vector2(0, 8));
        if (ActionButton(palette, "##open", "Open Project", new Vector2(w, 44), filled: false, Icon.Folder))
        {
            string? path = FileDialogs.OpenFile("Spot Project (*.sptproj)|*.sptproj");
            if (path != null)
            {
                _pendingOpen = path;
            }
        }

        if (_error != null)
        {
            ImGui.Dummy(new Vector2(0, 12));
            ImGui.PushTextWrapPos(0.0f);
            ImGui.TextColored(palette.LogError, _error);
            ImGui.PopTextWrapPos();
        }

        // Footer pinned to the bottom of the sidebar.
        float y = ImGui.GetWindowHeight() - ImGui.GetTextLineHeight() - 22;
        if (y > ImGui.GetCursorPosY()) ImGui.SetCursorPosY(y);
        ImGui.TextDisabled($"© {DateTime.Now.Year} Spot Engine");

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    // The "spot" brand mark: a rounded accent tile with an offset dot punched out of it.
    private void DrawLogo(EditorPalette palette)
    {
        var drawList = ImGui.GetWindowDrawList();
        Vector2 p0 = ImGui.GetCursorScreenPos();
        const float s = 56.0f;
        Vector2 p1 = p0 + new Vector2(s, s);

        drawList.AddRectFilled(p0, p1, ImGui.GetColorU32(palette.Accent), 14.0f);
        drawList.AddRectFilled(p0, p1, ImGui.GetColorU32(WithAlpha(Vector4.One, 0.06f)), 14.0f);
        // The "spot": a light dot offset toward the lower-right.
        drawList.AddCircleFilled(p0 + new Vector2(s * 0.62f, s * 0.62f), s * 0.16f,
            ImGui.GetColorU32(new Vector4(1, 1, 1, 0.95f)));
        drawList.AddCircle(p0 + new Vector2(s * 0.40f, s * 0.40f), s * 0.26f,
            ImGui.GetColorU32(WithAlpha(Vector4.One, 0.55f)), 0, 2.5f);

        ImGui.Dummy(new Vector2(s, s));
    }

    // ----- Right panel: recent projects -----------------------------------------------------------

    private void DrawRecentsPanel(EditorPalette palette)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(28, 24));
        ImGui.BeginChild("##recentsPanel", new Vector2(0, 0), ImGuiChildFlags.AlwaysUseWindowPadding);
        ImGui.PopStyleVar();

        ImGui.SetWindowFontScale(1.25f);
        ImGui.TextUnformatted("Recent Projects");
        ImGui.SetWindowFontScale(1.0f);
        if (_recent.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({_recent.Count})");
        }

        ImGui.Dummy(new Vector2(0, 6));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 8));

        // No AlwaysUseWindowPadding here: the scroll region is intentionally flush so cards line up
        // exactly under the header/separator (the panel's own padding already insets the whole column).
        ImGui.BeginChild("##recentsScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        if (_recent.Count == 0)
        {
            DrawEmptyRecents(palette);
        }
        else
        {
            foreach (var path in _recent)
            {
                DrawRecentCard(palette, path);
            }
        }

        ImGui.EndChild();
        ImGui.EndChild();
    }

    private void DrawEmptyRecents(EditorPalette palette)
    {
        Vector2 avail = ImGui.GetContentRegionAvail();
        const string line1 = "No recent projects yet";
        const string line2 = "Create a new project or open an existing one to get started.";

        float y = ImGui.GetCursorPosY() + avail.Y * 0.4f;
        ImGui.SetCursorPosY(y);
        CenteredText(avail.X, line1, palette.TextDisabled);
        ImGui.Dummy(new Vector2(0, 4));
        CenteredText(avail.X, line2, palette.TextDisabled);
    }

    private void DrawRecentCard(EditorPalette palette, string path)
    {
        var drawList = ImGui.GetWindowDrawList();
        const float cardH = 66.0f;

        ImGui.PushID(path);
        Vector2 p0 = ImGui.GetCursorScreenPos();
        float width = ImGui.GetContentRegionAvail().X;
        Vector2 size = new Vector2(width, cardH);

        ImGui.InvisibleButton("##card", size);
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();
        if (ImGui.IsItemClicked()) _pendingOpen = path;

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

        Vector2 p1 = p0 + size;
        Vector4 bg = active ? palette.FrameBgActive : hovered ? palette.FrameBgHovered : palette.FrameBg;
        drawList.AddRectFilled(p0, p1, ImGui.GetColorU32(bg), 8.0f);
        drawList.AddRect(p0, p1,
            ImGui.GetColorU32(hovered ? WithAlpha(palette.Accent, 0.7f) : palette.Border),
            8.0f, ImDrawFlags.None, hovered ? 1.5f : 1.0f);

        string name = Path.GetFileNameWithoutExtension(path);
        string dir2 = Path.GetDirectoryName(path) ?? path;

        // Letter avatar.
        const float av = 44.0f;
        Vector2 aMin = p0 + new Vector2(12, (cardH - av) * 0.5f);
        Vector2 aMax = aMin + new Vector2(av, av);
        drawList.AddRectFilled(aMin, aMax, ImGui.GetColorU32(WithAlpha(palette.Accent, 0.85f)), 9.0f);
        string initial = (name.Length > 0 ? char.ToUpperInvariant(name[0]) : '?').ToString();
        float initSize = 22.0f;
        float scale = initSize / ImGui.GetFontSize();
        Vector2 initTs = ImGui.CalcTextSize(initial) * scale;
        drawList.AddText(ImGui.GetFont(), initSize,
            aMin + (new Vector2(av, av) - initTs) * 0.5f,
            ImGui.GetColorU32(new Vector4(1, 1, 1, 0.95f)), initial);

        // Name + path.
        float textX = aMax.X + 14;
        float textW = p1.X - textX - 14;
        drawList.AddText(new Vector2(textX, p0.Y + 14),
            ImGui.GetColorU32(palette.Text), name);
        drawList.AddText(new Vector2(textX, p0.Y + 14 + ImGui.GetTextLineHeight() + 4),
            ImGui.GetColorU32(palette.TextDisabled), TruncateFront(dir2, textW));

        ImGui.Dummy(new Vector2(0, 8));
        ImGui.PopID();
    }

    // ----- Custom widgets -------------------------------------------------------------------------

    private enum Icon { None, Plus, Folder }

    private bool ActionButton(EditorPalette palette, string id, string label, Vector2 size, bool filled, Icon icon)
    {
        var drawList = ImGui.GetWindowDrawList();
        Vector2 p0 = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, size);
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();
        bool clicked = ImGui.IsItemClicked();
        Vector2 p1 = p0 + size;

        Vector4 bg = filled
            ? (active ? palette.AccentActive : hovered ? palette.AccentHovered : palette.Accent)
            : (active ? palette.FrameBgActive : hovered ? palette.FrameBgHovered : palette.FrameBg);
        drawList.AddRectFilled(p0, p1, ImGui.GetColorU32(bg), 8.0f);
        if (!filled)
        {
            drawList.AddRect(p0, p1, ImGui.GetColorU32(palette.Border), 8.0f, ImDrawFlags.None, 1.0f);
        }

        Vector4 fg = filled ? new Vector4(1, 1, 1, 1) : palette.Text;
        uint fgCol = ImGui.GetColorU32(fg);

        float cy = p0.Y + size.Y * 0.5f;
        float iconX = p0.X + 20;
        DrawButtonIcon(drawList, icon, new Vector2(iconX, cy), fgCol);

        Vector2 ts = ImGui.CalcTextSize(label);
        drawList.AddText(new Vector2(iconX + 30, cy - ts.Y * 0.5f), fgCol, label);

        return clicked;
    }

    private static void DrawButtonIcon(ImDrawListPtr drawList, Icon icon, Vector2 center, uint color)
    {
        switch (icon)
        {
            case Icon.Plus:
                drawList.AddLine(center - new Vector2(7, 0), center + new Vector2(7, 0), color, 2.0f);
                drawList.AddLine(center - new Vector2(0, 7), center + new Vector2(0, 7), color, 2.0f);
                break;
            case Icon.Folder:
                Vector2 bMin = center + new Vector2(-8, -3);
                Vector2 bMax = center + new Vector2(8, 6);
                drawList.AddRectFilled(bMin, bMax, color, 2.0f);
                drawList.AddRectFilled(center + new Vector2(-8, -6), center + new Vector2(-1, -3), color, 1.5f);
                break;
        }
    }

    private static void DrawSectionLabel(EditorPalette palette, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, WithAlpha(palette.TextDisabled, 0.9f));
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void CenteredText(float availWidth, string text, Vector4 color)
    {
        float tw = ImGui.CalcTextSize(text).X;
        float indent = MathF.Max(0, (availWidth - tw) * 0.5f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
        ImGui.TextColored(color, text);
    }

    // Ellipsizes a path from the front ("...\Projects\MyGame"), keeping the meaningful tail visible.
    private static string TruncateFront(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth) return text;
        float ellipsis = ImGui.CalcTextSize("...").X;
        for (int i = 0; i < text.Length; i++)
        {
            string candidate = text.Substring(i);
            if (ellipsis + ImGui.CalcTextSize(candidate).X <= maxWidth)
            {
                return "..." + candidate;
            }
        }
        return "...";
    }

    // ----- New-project modal ----------------------------------------------------------------------

    private void DrawNewProjectModal()
    {
        if (_isCreatingProject)
        {
            ImGui.OpenPopup("Create New Project");
        }

        // Center the modal over the launcher window.
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        bool open = true;
        if (ImGui.BeginPopupModal("Create New Project", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.SetNextItemWidth(360);
            ImGui.InputText("Project Name", ref _newProjectName, 128);
            ImGui.SetNextItemWidth(360);
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
                _isCreatingProject = false;
                ImGui.CloseCurrentPopup();
                StartCreate(_newProjectName.Trim(), _newProjectLocation);
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

    // ----- Loading hand-off -----------------------------------------------------------------------

    // Begins the loading cover, deferring the heavy open work until the cover has been presented.
    private void StartOpen(string sptprojPath)
    {
        BeginLoading(Path.GetFileNameWithoutExtension(sptprojPath), "Opening project...", () =>
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
                RecentProjects.Add(sptproj);
                OpenEditor();
            }
            catch (System.Exception ex)
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

    // ----- Color helpers --------------------------------------------------------------------------

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    private static Vector4 Darken(Vector4 c, float amount) =>
        new(
            MathF.Max(0, c.X - amount),
            MathF.Max(0, c.Y - amount),
            MathF.Max(0, c.Z - amount),
            c.W);
}
