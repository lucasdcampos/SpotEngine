using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Spot.Core;
using Spot.Build;
using Spot.Scenes;
using Spot.DebugUI.UI;
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
    private const int LauncherWidth = 840;
    private const int LauncherHeight = 520;
    private const float SidebarWidth = 250.0f;

    private const string RepoUrl = "https://github.com/lucasdcampos/spotengine";

    // Cover frames to present before running the heavy open/create work. Enough for the editor-sized
    // window (requested when loading begins) to finish resizing so the freeze that follows happens
    // under a full-size loading screen instead of the old, small one.
    private const int LoadingSettleFrames = 2;

    private List<RecentProject> _recent = new();
    private string _search = string.Empty;

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
        native.Title = $"Spot {Application.Instance.EngineVersion}";

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
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24, 26));
        ImGui.BeginChild("##sidebar", new Vector2(SidebarWidth, 0), ImGuiChildFlags.AlwaysUseWindowPadding);
        ImGui.PopStyleVar();

        DrawLogo(palette);

        ImGui.Dummy(new Vector2(0, 14));
        ImGui.SetWindowFontScale(1.5f);
        ImGui.TextUnformatted("Spot Engine");
        ImGui.SetWindowFontScale(1.0f);
        ImGui.TextDisabled("2D / 3D Game Engine");

        ImGui.Dummy(new Vector2(0, 6));
        DrawVersionPill(palette);

        ImGui.Dummy(new Vector2(0, 24));
        DrawSectionLabel(palette, "START");
        ImGui.Dummy(new Vector2(0, 6));

        float w = ImGui.GetContentRegionAvail().X;
        if (ActionButton(palette, "##new", "New Project", new Vector2(w, 42), filled: true, EditorIcons.Cube))
        {
            _isCreatingProject = true;
        }
        ImGui.Dummy(new Vector2(0, 8));
        if (ActionButton(palette, "##open", "Open Project", new Vector2(w, 42), filled: false, EditorIcons.FolderOpen))
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

        DrawSidebarFooter(palette);

        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    // Resource links + copyright, pinned to the bottom of the sidebar.
    private void DrawSidebarFooter(EditorPalette palette)
    {
        // Anchor the footer to the bottom of the content region (which already excludes the child's
        // bottom padding) rather than the raw window height — ending exactly at the content boundary
        // keeps the sidebar from overflowing by a few pixels, which is what showed a scrollbar. When the
        // window is genuinely too short, this Y falls above the cursor and the pin is skipped, letting
        // the content flow (and the scrollbar appear) only then.
        float line = ImGui.GetTextLineHeight();
        float sp = ImGui.GetStyle().ItemSpacing.Y;
        float footerH = 3 * line + 24 + 5 * sp; // separator + spacer + two links + spacer + copyright
        float y = ImGui.GetWindowContentRegionMax().Y - footerH;
        if (y > ImGui.GetCursorPosY()) ImGui.SetCursorPosY(y);

        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 8));
        LinkLabel(palette, EditorIcons.Code, "GitHub Repository", RepoUrl);
        LinkLabel(palette, EditorIcons.File, "Documentation", $"{RepoUrl}/tree/master/docs");
        ImGui.Dummy(new Vector2(0, 6));
        ImGui.TextDisabled($"© {DateTime.Now.Year} Spot Engine  ·  v{Application.Instance.EngineVersion}");
    }

    // A small rounded "v0.1.0" chip, tinted with the accent, sitting under the engine title.
    private void DrawVersionPill(EditorPalette palette)
    {
        var drawList = ImGui.GetWindowDrawList();
        string text = $"v{Application.Instance.EngineVersion}";
        Vector2 pos = ImGui.GetCursorScreenPos();
        float width = DrawPill(drawList, pos, text, WithAlpha(palette.Text, 0.07f), palette.TextDisabled);
        ImGui.Dummy(new Vector2(width, ImGui.GetTextLineHeight() + 6));
    }

    // The "spot" brand mark: a rounded accent tile with an offset dot punched out of it.
    private void DrawLogo(EditorPalette palette)
    {
        var drawList = ImGui.GetWindowDrawList();
        Vector2 p0 = ImGui.GetCursorScreenPos();
        const float s = 52.0f;
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
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(26, 22));
        ImGui.BeginChild("##recentsPanel", new Vector2(0, 0), ImGuiChildFlags.AlwaysUseWindowPadding);
        ImGui.PopStyleVar();

        // Header: title (+ count) on the left, a search filter on the right.
        float headerTop = ImGui.GetCursorPosY();
        ImGui.SetWindowFontScale(1.2f);
        ImGui.TextUnformatted("Recent Projects");
        ImGui.SetWindowFontScale(1.0f);
        if (_recent.Count > 0)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled($"({_recent.Count})");
        }

        const float searchW = 210.0f;
        if (_recent.Count > 0)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - searchW);
            ImGui.SetCursorPosY(headerTop - 2);
            ImGui.SetNextItemWidth(searchW);
            ImGui.InputTextWithHint("##search", $"{EditorIcons.Search}  Search projects", ref _search, 128);
        }

        ImGui.Dummy(new Vector2(0, 8));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 8));

        // No AlwaysUseWindowPadding here: the scroll region is intentionally flush so cards line up
        // exactly under the header/separator (the panel's own padding already insets the whole column).
        ImGui.BeginChild("##recentsScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        var filtered = FilterRecents();
        if (_recent.Count == 0)
        {
            DrawEmptyRecents(palette, "No recent projects yet",
                "Create a new project or open an existing one to get started.");
        }
        else if (filtered.Count == 0)
        {
            DrawEmptyRecents(palette, "No matches",
                $"No recent project matches \"{_search}\".");
        }
        else
        {
            foreach (var project in filtered)
            {
                DrawRecentCard(palette, project);
            }
        }

        ImGui.EndChild();
        ImGui.EndChild();
    }

    private List<RecentProject> FilterRecents()
    {
        if (string.IsNullOrWhiteSpace(_search)) return _recent;
        string q = _search.Trim();
        return _recent.Where(p =>
                Path.GetFileNameWithoutExtension(p.Path).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Path.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void DrawEmptyRecents(EditorPalette palette, string line1, string line2)
    {
        Vector2 avail = ImGui.GetContentRegionAvail();

        float y = ImGui.GetCursorPosY() + avail.Y * 0.36f;
        ImGui.SetCursorPosY(y);
        ImGui.SetWindowFontScale(1.15f);
        CenteredText(avail.X, line1, palette.Text);
        ImGui.SetWindowFontScale(1.0f);
        ImGui.Dummy(new Vector2(0, 6));
        CenteredText(avail.X, line2, palette.TextDisabled);
    }

    private void DrawRecentCard(EditorPalette palette, RecentProject project)
    {
        string path = project.Path;
        var drawList = ImGui.GetWindowDrawList();
        const float cardH = 74.0f;
        const float actionSize = 30.0f;

        ImGui.PushID(path);
        Vector2 p0 = ImGui.GetCursorScreenPos();
        float width = ImGui.GetContentRegionAvail().X;
        Vector2 size = new Vector2(width, cardH);
        Vector2 p1 = p0 + size;

        // Allow the on-hover action buttons (drawn on top) to take mouse priority over the card body.
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton("##card", size);
        bool active = ImGui.IsItemActive();
        bool rowHovered = ImGui.IsMouseHoveringRect(p0, p1);
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

        Vector4 bg = active ? palette.FrameBgActive : rowHovered ? palette.FrameBgHovered : palette.FrameBg;
        drawList.AddRectFilled(p0, p1, ImGui.GetColorU32(bg), 8.0f);
        drawList.AddRect(p0, p1,
            ImGui.GetColorU32(rowHovered ? WithAlpha(new Vector4(1, 1, 1, 1), 0.22f) : palette.Border),
            8.0f, ImDrawFlags.None, rowHovered ? 1.5f : 1.0f);

        string name = Path.GetFileNameWithoutExtension(path);
        string dir2 = Path.GetDirectoryName(path) ?? path;

        // Letter avatar — a neutral surface tile so the list stays calm and monochrome.
        const float av = 46.0f;
        Vector2 aMin = p0 + new Vector2(13, (cardH - av) * 0.5f);
        Vector2 aMax = aMin + new Vector2(av, av);
        drawList.AddRectFilled(aMin, aMax, ImGui.GetColorU32(WithAlpha(palette.Text, 0.08f)), 10.0f);
        drawList.AddRect(aMin, aMax, ImGui.GetColorU32(palette.Border), 10.0f);
        string initial = (name.Length > 0 ? char.ToUpperInvariant(name[0]) : '?').ToString();
        const float initSize = 22.0f;
        Vector2 initTs = ImGui.CalcTextSize(initial) * (initSize / ImGui.GetFontSize());
        drawList.AddText(ImGui.GetFont(), initSize,
            aMin + (new Vector2(av, av) - initTs) * 0.5f,
            ImGui.GetColorU32(WithAlpha(palette.Text, 0.78f)), initial);

        // Text column: name, path, then a meta row (last-opened + engine-version chip).
        float textX = aMin.X + av + 14;
        float actionsX = p1.X - 12 - actionSize * 2 - 6;
        float textW = actionsX - textX - 10;

        drawList.AddText(new Vector2(textX, p0.Y + 11),
            ImGui.GetColorU32(palette.Text), name);
        drawList.AddText(new Vector2(textX, p0.Y + 11 + ImGui.GetTextLineHeight() + 3),
            ImGui.GetColorU32(palette.TextDisabled), TruncateFront(dir2, textW));

        float metaY = p0.Y + cardH - ImGui.GetTextLineHeight() - 11;
        string relative = $"Opened {RelativeTime(project.LastOpenedUtc)}";
        drawList.AddText(new Vector2(textX, metaY), ImGui.GetColorU32(palette.TextDisabled), relative);
        if (!string.IsNullOrEmpty(project.EngineVersion))
        {
            float metaX = textX + ImGui.CalcTextSize(relative).X + 10;
            DrawPill(drawList, new Vector2(metaX, metaY - 3), $"v{project.EngineVersion}",
                WithAlpha(palette.Text, 0.08f), palette.TextDisabled);
        }

        // Hover-revealed quick actions on the right edge (open the containing folder, or remove).
        if (rowHovered)
        {
            float ay = p0.Y + (cardH - actionSize) * 0.5f;
            if (IconButton("##openfolder", EditorIcons.FolderOpen,
                    new Vector2(actionsX, ay), actionSize, palette, "Open containing folder"))
            {
                string? dir = Path.GetDirectoryName(path);
                if (dir != null) OpenExternally(dir);
            }
            if (IconButton("##remove", EditorIcons.Times,
                    new Vector2(actionsX + actionSize + 6, ay), actionSize, palette, "Remove from recents", danger: true))
            {
                _pendingRemove = path;
            }
        }

        // Restore the layout cursor below the card (the action buttons moved it) and add the gap.
        ImGui.SetCursorScreenPos(new Vector2(p0.X, p1.Y));
        ImGui.Dummy(new Vector2(0, 8));
        ImGui.PopID();
    }

    // ----- Custom widgets -------------------------------------------------------------------------

    // A primary action button (filled = accent, else outline) with a leading Font Awesome glyph.
    private bool ActionButton(EditorPalette palette, string id, string label, Vector2 size, bool filled, string glyph)
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
            drawList.AddRect(p0, p1,
                ImGui.GetColorU32(hovered ? WithAlpha(new Vector4(1, 1, 1, 1), 0.22f) : palette.Border),
                8.0f, ImDrawFlags.None, 1.0f);
        }

        Vector4 fg = filled ? new Vector4(1, 1, 1, 1) : palette.Text;
        uint fgCol = ImGui.GetColorU32(fg);

        float cy = p0.Y + size.Y * 0.5f;
        float iconX = p0.X + 20;
        Vector2 gs = ImGui.CalcTextSize(glyph);
        drawList.AddText(new Vector2(iconX, cy - gs.Y * 0.5f), fgCol, glyph);

        Vector2 ts = ImGui.CalcTextSize(label);
        drawList.AddText(new Vector2(iconX + 28, cy - ts.Y * 0.5f), fgCol, label);

        return clicked;
    }

    // A square, icon-only button drawn at an absolute position (used for on-hover card actions).
    private bool IconButton(string id, string glyph, Vector2 topLeft, float size, EditorPalette palette, string tooltip, bool danger = false)
    {
        var drawList = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(topLeft);
        ImGui.InvisibleButton(id, new Vector2(size, size));
        bool hovered = ImGui.IsItemHovered();
        bool active = ImGui.IsItemActive();
        bool clicked = ImGui.IsItemClicked();

        Vector2 max = topLeft + new Vector2(size, size);
        if (hovered)
        {
            drawList.AddRectFilled(topLeft, max,
                ImGui.GetColorU32(active ? palette.FrameBgActive : palette.FrameBgHovered), 6.0f);
        }

        Vector4 fg = danger && hovered ? palette.LogError : hovered ? palette.Text : palette.TextDisabled;
        Vector2 ts = ImGui.CalcTextSize(glyph);
        drawList.AddText(topLeft + (new Vector2(size, size) - ts) * 0.5f, ImGui.GetColorU32(fg), glyph);
        if (hovered) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    // A clickable text link (icon + label) that opens a URL and lights up on hover.
    private void LinkLabel(EditorPalette palette, string glyph, string text, string url)
    {
        string full = $"{glyph}  {text}";
        Vector2 ts = ImGui.CalcTextSize(full);
        Vector2 p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##link_{text}", new Vector2(ts.X + 4, ts.Y + 4));
        bool hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked()) OpenExternally(url);
        ImGui.GetWindowDrawList().AddText(p,
            ImGui.GetColorU32(hovered ? palette.Text : palette.TextDisabled), full);
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }

    // Draws a rounded "chip" with centered text; returns the chip's total width.
    private static float DrawPill(ImDrawListPtr drawList, Vector2 pos, string text, Vector4 bg, Vector4 fg)
    {
        Vector2 ts = ImGui.CalcTextSize(text);
        const float padX = 8.0f;
        float h = ts.Y + 5.0f;
        Vector2 max = pos + new Vector2(ts.X + padX * 2, h);
        drawList.AddRectFilled(pos, max, ImGui.GetColorU32(bg), h * 0.5f);
        drawList.AddText(pos + new Vector2(padX, (h - ts.Y) * 0.5f), ImGui.GetColorU32(fg), text);
        return max.X - pos.X;
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

    // A short, human "time ago" phrase for a project's last-opened timestamp.
    private static string RelativeTime(DateTime? utc)
    {
        if (utc == null) return "recently";

        TimeSpan span = DateTime.UtcNow - utc.Value;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}w ago";
        return utc.Value.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
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
            ImGui.TextDisabled("Give your project a name and a home on disk.");
            ImGui.Dummy(new Vector2(0, 6));

            ImGui.SetNextItemWidth(380);
            ImGui.InputText("Project Name", ref _newProjectName, 128);
            ImGui.SetNextItemWidth(380);
            ImGui.InputText("Location", ref _newProjectLocation, 256);
            ImGui.SameLine();
            if (ImGui.Button($"{EditorIcons.FolderOpen}##Location"))
            {
                string? folder = FileDialogs.SelectFolder();
                if (folder != null) _newProjectLocation = folder;
            }

            // Show where the project folder will actually land so there are no surprises.
            string name = _newProjectName.Trim();
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrWhiteSpace(_newProjectLocation))
            {
                ImGui.Dummy(new Vector2(0, 2));
                ImGui.TextDisabled($"{EditorIcons.Folder}  {Path.Combine(_newProjectLocation, name)}");
            }

            ImGui.Dummy(new Vector2(0, 4));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0, 4));

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
