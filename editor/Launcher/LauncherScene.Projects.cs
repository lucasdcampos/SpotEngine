using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ImGuiNET;
using Spot.DebugUI.UI;
using Spot.Editor.Utils;
using Spot.Engine;
using Spot.Framework.Graphics;
using static Spot.Editor.Launcher.LauncherColors;
using static Spot.Editor.Launcher.LauncherWidgets;

namespace Spot.Editor.Launcher;

// Right column: the header (title, search, sort, view) over the project cards, in a grid or a list.
public partial class LauncherScene
{
    private const string ProjectMenuId = "##projectMenu";
    private const string SortMenuId = "##sortMenu";

    private string _search = string.Empty;
    private bool _focusSearch;

    private string? _selectedPath;
    private bool _scrollToSelection;
    private int _columns = 1; // grid columns last laid out, for Up/Down

    // The card menu is one popup shared by every card; a card requests it and the list opens it after drawing.
    private string? _menuPath;
    private string? _menuOpenFor;
    private bool _menuRequested;
    private Vector2 _menuAnchor;
    private Vector2 _menuPivot;

    private bool _sortMenuRequested;
    private Vector2 _sortMenuAnchor;

    private void DrawMain(in LauncherColors c, List<RecentProject> visible)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(LauncherLayout.MainPaddingX, LauncherLayout.TopInset));
        ImGui.BeginChild("##main", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar();

        float width = ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        DrawHeader(c, visible, width);
        ImGui.Dummy(new Vector2(0, Space.Xl));
        if (_error != null)
        {
            DrawErrorBanner(c, width);
            ImGui.Dummy(new Vector2(0, Space.Lg));
        }
        ImGui.PopStyleVar();

        DrawSortMenu();
        DrawProjectArea(c, visible, width);

        ImGui.EndChild();
    }

    // ----- Header ------------------------------------------------------------------------------------------

    private void DrawHeader(in LauncherColors c, List<RecentProject> visible, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        Vector2 origin = ImGui.GetCursorScreenPos();
        float h = LauncherLayout.HeaderHeight;

        ImFontPtr heading = EditorFonts.Heading;
        const string title = "Projects";
        Vector2 titleSize = Measure(heading, title);
        Text(dl, heading, new Vector2(origin.X, origin.Y + (h - heading.FontSize) * 0.5f), c.Text, title);
        float titleRight = origin.X + titleSize.X;

        if (_recent.Count > 0)
        {
            // While searching, the count reads "3 of 12".
            string count = visible.Count == _recent.Count
                ? _recent.Count.ToString(CultureInfo.InvariantCulture)
                : $"{visible.Count} of {_recent.Count}";
            ImFontPtr small = EditorFonts.Small;
            float pillHeight = small.FontSize + 4.0f;
            titleRight += Space.Sm + Pill(dl, new Vector2(titleRight + Space.Sm, origin.Y + (h - pillHeight) * 0.5f),
                small, count, c.Fill, c.TextMuted);

            DrawHeaderControls(c, origin, width, titleRight);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, h));
    }

    // Search, sort and view, right-aligned on the title row. The search box gives up width first when narrow.
    private void DrawHeaderControls(in LauncherColors c, Vector2 origin, float width, float titleRight)
    {
        string sortLabel = SortLabel(_preferences.Sort);
        float sortWidth = DropdownWidth(EditorIcons.Sort, sortLabel);
        float viewWidth = 2 * (LauncherLayout.ControlHeight + Space.Xs);
        float right = origin.X + width;
        float roomForSearch = right - viewWidth - sortWidth - 2 * Space.Sm - (titleRight + Space.Xl);
        float searchWidth = Math.Clamp(roomForSearch, 120.0f, 240.0f);

        float y = origin.Y + (LauncherLayout.HeaderHeight - LauncherLayout.ControlHeight) * 0.5f;
        float x = right - viewWidth - Space.Sm - sortWidth - Space.Sm - searchWidth;

        ImGui.SetCursorScreenPos(new Vector2(x, y));
        SearchField("##search", searchWidth < 170.0f ? "Search" : "Search projects", ref _search, searchWidth, c, _focusSearch);
        _focusSearch = false;

        ImGui.SetCursorScreenPos(new Vector2(x + searchWidth + Space.Sm, y));
        if (DropdownButton("##sort", EditorIcons.Sort, sortLabel, c, out Vector2 sortBottomLeft))
        {
            _sortMenuRequested = true;
            _sortMenuAnchor = sortBottomLeft + new Vector2(0, Space.Xs);
        }

        ImGui.SetCursorScreenPos(new Vector2(right - viewWidth, y));
        int view = Segmented("##view", new[] { EditorIcons.Grid, EditorIcons.List },
            new[] { "Grid view", "List view" }, (int)_preferences.View, c);
        if (view != (int)_preferences.View)
        {
            _preferences.View = (ProjectView)view;
            _preferences.Save();
        }
    }

    private static string SortLabel(ProjectSort sort) => sort == ProjectSort.Name ? "Name" : "Last opened";

    private void DrawSortMenu()
    {
        if (_sortMenuRequested)
        {
            ImGui.OpenPopup(SortMenuId);
            ImGui.SetNextWindowPos(_sortMenuAnchor);
            _sortMenuRequested = false;
        }

        PushMenuStyle();
        if (ImGui.BeginPopup(SortMenuId))
        {
            ImGui.PushFont(EditorFonts.Small);
            ImGui.TextDisabled("Sort by");
            ImGui.PopFont();
            foreach (ProjectSort sort in new[] { ProjectSort.LastOpened, ProjectSort.Name })
            {
                if (ImGui.MenuItem(SortLabel(sort), null, _preferences.Sort == sort) && _preferences.Sort != sort)
                {
                    _preferences.Sort = sort;
                    _preferences.Save();
                }
            }
            ImGui.EndPopup();
        }
        PopMenuStyle();
    }

    // A dismissible strip for the last failure (a project that would not open or could not be created).
    private void DrawErrorBanner(in LauncherColors c, float width)
    {
        const float h = 36.0f;
        var dl = ImGui.GetWindowDrawList();
        Vector2 min = ImGui.GetCursorScreenPos();
        Vector2 max = min + new Vector2(width, h);
        dl.AddRectFilled(min, max, U32(WithAlpha(c.Danger, 0.12f)), LauncherLayout.ControlRounding);
        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), U32(WithAlpha(c.Danger, 0.35f)),
            LauncherLayout.ControlRounding);

        ImFontPtr font = EditorFonts.Body;
        float y = min.Y + (h - font.FontSize) * 0.5f;
        float glyphWidth = Measure(font, EditorIcons.Warning).X;
        Text(dl, font, new Vector2(min.X + Space.Md, y), c.Danger, EditorIcons.Warning);

        float textX = min.X + Space.Md + glyphWidth + Space.Sm;
        float closeSize = h - Space.Sm;
        string message = _error ?? string.Empty;
        string shown = EllipsizeEnd(font, message, max.X - Space.Xs - closeSize - Space.Sm - textX);
        ImGui.SetCursorScreenPos(new Vector2(textX, min.Y));
        ImGui.InvisibleButton("##errorText", new Vector2(max.X - closeSize - Space.Sm - textX, h));
        if (shown != message) Tooltip(message);
        Text(dl, font, new Vector2(textX, y), c.Text, shown);

        ImGui.SetCursorScreenPos(new Vector2(max.X - Space.Xs - closeSize, min.Y + (h - closeSize) * 0.5f));
        if (IconButton("##dismissError", EditorIcons.Times, new Vector2(closeSize, closeSize), c, "Dismiss"))
        {
            _error = null;
        }
        ImGui.SetCursorScreenPos(min);
        ImGui.Dummy(new Vector2(width, h));
    }

    // ----- Project area ------------------------------------------------------------------------------------

    private void DrawProjectArea(in LauncherColors c, List<RecentProject> visible, float width)
    {
        // The scroll region runs on to the window's right and bottom edges: its scrollbar sits in the right
        // gutter instead of eating into the cards, and cards scroll out under the bottom edge. Cards keep the
        // header's width, so they stay aligned with it whether or not the scrollbar shows.
        float height = ImGui.GetContentRegionAvail().Y + LauncherLayout.TopInset;
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, Space.Sm);
        ImGui.BeginChild("##projects", new Vector2(width + LauncherLayout.MainPaddingX, height), ImGuiChildFlags.None,
            ImGuiWindowFlags.NoBackground);
        ImGui.PopStyleVar();

        if (_recent.Count == 0)
        {
            DrawEmptyState(c, width, height);
        }
        else if (visible.Count == 0)
        {
            DrawNoMatches(c, width, height);
        }
        else
        {
            _menuOpenFor = ImGui.IsPopupOpen(ProjectMenuId) ? _menuPath : null;

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            if (_preferences.View == ProjectView.Grid) DrawGrid(c, visible, width);
            else DrawList(c, visible, width);
            ImGui.PopStyleVar();

            // A click on empty space between cards clears the selection, as in a file browser.
            if (ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _selectedPath = null;
            }

            DrawProjectMenu();
        }
        _scrollToSelection = false;

        ImGui.EndChild();
    }

    private void DrawGrid(in LauncherColors c, List<RecentProject> projects, float width)
    {
        float gap = LauncherLayout.TileGap;
        int columns = Math.Max(1, (int)((width + gap) / (LauncherLayout.TileMinWidth + gap)));
        _columns = columns;

        float tileWidth = (width - gap * (columns - 1)) / columns;
        float thumbHeight = MathF.Round(tileWidth / LauncherLayout.ThumbnailAspect);
        float tileHeight = thumbHeight + TileCaptionHeight();
        Vector2 origin = ImGui.GetCursorScreenPos();

        for (int i = 0; i < projects.Count; i++)
        {
            int column = i % columns, row = i / columns;
            float left = origin.X + column * (tileWidth + gap);
            float top = origin.Y + row * (tileHeight + gap);
            DrawTile(c, projects[i], new Vector2(MathF.Round(left), top), new Vector2(MathF.Round(left + tileWidth), top + tileHeight),
                thumbHeight);
        }

        int rows = (projects.Count + columns - 1) / columns;
        ImGui.SetCursorScreenPos(origin + new Vector2(0, rows * tileHeight + (rows - 1) * gap));
        ImGui.Dummy(new Vector2(width, LauncherLayout.BottomInset));
    }

    private void DrawList(in LauncherColors c, List<RecentProject> projects, float width)
    {
        _columns = 1;
        Vector2 cursor = ImGui.GetCursorScreenPos();
        foreach (RecentProject project in projects)
        {
            DrawRow(c, project, cursor, cursor + new Vector2(width, LauncherLayout.RowHeight));
            cursor.Y += LauncherLayout.RowHeight + LauncherLayout.RowGap;
        }
        ImGui.SetCursorScreenPos(cursor);
        ImGui.Dummy(new Vector2(width, LauncherLayout.BottomInset - LauncherLayout.RowGap));
    }

    // Name, path and meta lines under a grid tile's picture.
    private static float TileCaptionHeight() =>
        LauncherLayout.TilePadding + EditorFonts.Title.FontSize + Space.Xs + EditorFonts.Small.FontSize + 3.0f +
        EditorFonts.Small.FontSize + LauncherLayout.TilePadding;

    // ----- Cards -------------------------------------------------------------------------------------------

    // A grid tile: the picture on top, then the name with its "..." button, the folder, and when it was opened.
    private void DrawTile(in LauncherColors c, RecentProject project, Vector2 min, Vector2 max, float thumbHeight)
    {
        ImGui.PushID(project.Path);
        CardState state = CardInteraction(project, min, max);
        var dl = ImGui.GetWindowDrawList();
        float rounding = LauncherLayout.CardRounding;

        dl.AddRectFilled(min, max, U32(CardFill(c, state)), rounding);
        DrawThumbnail(dl, project, min, new Vector2(max.X, min.Y + thumbHeight), rounding, ImDrawFlags.RoundCornersTop);

        ImFontPtr title = EditorFonts.Title, small = EditorFonts.Small;
        float pad = LauncherLayout.TilePadding;
        float left = min.X + pad, right = max.X - pad;
        float y = min.Y + thumbHeight + pad;

        const float menuSize = 24.0f;
        ImGui.SetCursorScreenPos(new Vector2(right - menuSize + Space.Xs, y + (title.FontSize - menuSize) * 0.5f));
        MenuButton(c, project, state, new Vector2(menuSize, menuSize));

        Text(dl, title, new Vector2(left, y), c.Text, EllipsizeEnd(title, ProjectName(project), right - left - menuSize));
        y += title.FontSize + Space.Xs;
        Text(dl, small, new Vector2(left, y), c.TextMuted, EllipsizePath(small, ProjectDirectory(project), right - left));
        y += small.FontSize + 3.0f;

        string? opened = RelativeTime(project.LastOpenedUtc);
        if (opened != null) Text(dl, small, new Vector2(left, y), c.TextFaint, $"Opened {opened}");
        DrawVersion(dl, c, project, right, y, alignRight: true);

        DrawCardBorder(dl, c, state, min, max);
        ImGui.PopID();
    }

    // A list row: a small picture, name over folder, then when it was opened, its engine version, and "...".
    private void DrawRow(in LauncherColors c, RecentProject project, Vector2 min, Vector2 max)
    {
        ImGui.PushID(project.Path);
        CardState state = CardInteraction(project, min, max);
        var dl = ImGui.GetWindowDrawList();
        float height = max.Y - min.Y;

        dl.AddRectFilled(min, max, U32(CardFill(c, state)), LauncherLayout.CardRounding);

        var thumbSize = new Vector2(LauncherLayout.RowThumbnailWidth, MathF.Round(LauncherLayout.RowThumbnailWidth / LauncherLayout.ThumbnailAspect));
        float inset = MathF.Round((height - thumbSize.Y) * 0.5f);
        Vector2 thumbMin = min + new Vector2(inset, inset);
        DrawThumbnail(dl, project, thumbMin, thumbMin + thumbSize, LauncherLayout.CardRounding - inset * 0.5f, ImDrawFlags.None);
        dl.AddRect(thumbMin, thumbMin + thumbSize, U32(c.CardBorder), LauncherLayout.CardRounding - inset * 0.5f);

        // Columns from the right edge: "...", engine version, last opened.
        const float menuSize = 28.0f, versionColumn = 64.0f, openedColumn = 128.0f;
        float menuX = max.X - Space.Md - menuSize;
        float versionX = menuX - Space.Lg - versionColumn;
        float openedX = versionX - Space.Xl - openedColumn;

        ImFontPtr title = EditorFonts.Title, small = EditorFonts.Small;
        float textX = thumbMin.X + thumbSize.X + Space.Lg;
        float textWidth = openedX - Space.Xl - textX;
        float blockTop = min.Y + MathF.Round((height - (title.FontSize + Space.Xs + small.FontSize)) * 0.5f);
        Text(dl, title, new Vector2(textX, blockTop), c.Text, EllipsizeEnd(title, ProjectName(project), textWidth));
        Text(dl, small, new Vector2(textX, blockTop + title.FontSize + Space.Xs), c.TextMuted,
            EllipsizePath(small, ProjectDirectory(project), textWidth));

        float metaY = min.Y + MathF.Round((height - small.FontSize) * 0.5f);
        string? opened = RelativeTime(project.LastOpenedUtc);
        if (opened != null) Text(dl, small, new Vector2(openedX, metaY), c.TextMuted, Capitalize(opened));
        DrawVersion(dl, c, project, versionX, metaY, alignRight: false);

        ImGui.SetCursorScreenPos(new Vector2(menuX, min.Y + (height - menuSize) * 0.5f));
        MenuButton(c, project, state, new Vector2(menuSize, menuSize));

        DrawCardBorder(dl, c, state, min, max);
        ImGui.PopID();
    }

    private readonly record struct CardState(bool Hovered, bool Selected, bool MenuOpen)
    {
        public bool Lit => Hovered || MenuOpen;
    }

    // Submits a card's hit area: click selects, double-click opens, right-click opens the menu at the mouse.
    // It allows overlap so the "..." button drawn over it keeps its own clicks.
    private CardState CardInteraction(RecentProject project, Vector2 min, Vector2 max)
    {
        ImGui.SetCursorScreenPos(min);
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton("##card", max - min);
        bool itemHovered = ImGui.IsItemHovered();

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) Select(project.Path);
        if (itemHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) _pendingOpen = project.Path;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            Select(project.Path);
            RequestMenu(project.Path, ImGui.GetMousePos(), Vector2.Zero);
        }
        Tooltip(CardTooltip(project));

        if (_scrollToSelection && project.Path == _selectedPath) ScrollIntoView(min, max);

        // The card stays lit while the mouse is over its "..." button too (which takes the item hover).
        bool hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(min, max);
        return new CardState(hovered, project.Path == _selectedPath, project.Path == _menuOpenFor);
    }

    private void MenuButton(in LauncherColors c, RecentProject project, CardState state, Vector2 size)
    {
        Vector2 min = ImGui.GetCursorScreenPos();
        if (IconButton("##more", EditorIcons.Ellipsis, size, c, "More actions", state.MenuOpen))
        {
            Select(project.Path);
            // Drop down from the button, right-aligned with it.
            RequestMenu(project.Path, new Vector2(min.X + size.X, min.Y + size.Y + Space.Xs), new Vector2(1, 0));
        }
    }

    private static Vector4 CardFill(in LauncherColors c, CardState state) =>
        state.Selected ? (state.Lit ? Mix(c.CardSelected, c.Text, 0.03f) : c.CardSelected)
        : state.Lit ? c.CardHovered : c.Card;

    // Drawn last, over the picture, so the edge stays crisp where the image meets the card.
    private static void DrawCardBorder(ImDrawListPtr dl, in LauncherColors c, CardState state, Vector2 min, Vector2 max)
    {
        if (state.Selected)
        {
            dl.AddRect(min + new Vector2(0.75f), max - new Vector2(0.75f), U32(c.Accent), LauncherLayout.CardRounding,
                ImDrawFlags.None, 1.5f);
        }
        else
        {
            dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), U32(state.Lit ? c.CardBorderHovered : c.CardBorder),
                LauncherLayout.CardRounding);
        }
    }

    private static void ScrollIntoView(Vector2 min, Vector2 max)
    {
        float top = ImGui.GetWindowPos().Y;
        float bottom = top + ImGui.GetWindowHeight();
        if (min.Y < top) ImGui.SetScrollY(ImGui.GetScrollY() - (top - min.Y) - Space.Sm);
        else if (max.Y > bottom) ImGui.SetScrollY(ImGui.GetScrollY() + (max.Y - bottom) + Space.Lg);
    }

    // The captured viewport picture, cropped to fill the slot, or the placeholder until it has loaded.
    private void DrawThumbnail(ImDrawListPtr dl, RecentProject project, Vector2 min, Vector2 max, float rounding, ImDrawFlags corners)
    {
        bool loaded = _thumbnails.TryGet(ProjectDirectory(project), out Texture2D texture, out float opacity);
        if (!loaded || opacity < 1.0f)
        {
            ProjectPlaceholder.Draw(dl, ProjectName(project), min, max, rounding, corners);
        }
        if (!loaded) return;

        float slotAspect = (max.X - min.X) / (max.Y - min.Y);
        float imageAspect = texture.Width / (float)texture.Height;
        Vector2 uvMin = Vector2.Zero, uvMax = Vector2.One;
        if (imageAspect > slotAspect)
        {
            float margin = (1.0f - slotAspect / imageAspect) * 0.5f;
            uvMin.X = margin;
            uvMax.X = 1.0f - margin;
        }
        else
        {
            float margin = (1.0f - imageAspect / slotAspect) * 0.5f;
            uvMin.Y = margin;
            uvMax.Y = 1.0f - margin;
        }

        // Textures are stored bottom-up, so V runs from the image's bottom (0) to its top (1).
        dl.AddImageRounded((IntPtr)texture.Handle.Id, min, max, new Vector2(uvMin.X, uvMax.Y), new Vector2(uvMax.X, uvMin.Y),
            U32(new Vector4(1, 1, 1, opacity)), rounding, corners);
    }

    // The engine version that last opened the project; amber when it differs from this editor's.
    private static void DrawVersion(ImDrawListPtr dl, in LauncherColors c, RecentProject project, float x, float y, bool alignRight)
    {
        if (string.IsNullOrEmpty(project.EngineVersion)) return;
        ImFontPtr small = EditorFonts.Small;
        string label = $"v{project.EngineVersion}";
        bool mismatch = !IsCurrentVersion(project);
        if (alignRight) x -= Measure(small, label).X;
        Text(dl, small, new Vector2(x, y), mismatch ? c.Warning : c.TextFaint, label);
    }

    private static bool IsCurrentVersion(RecentProject project) =>
        string.IsNullOrEmpty(project.EngineVersion) || project.EngineVersion == Application.Instance.EngineVersion;

    private static string CardTooltip(RecentProject project)
    {
        string tip = project.Path;
        if (project.LastOpenedUtc is DateTime utc)
        {
            tip += $"\nLast opened {utc.ToLocalTime().ToString("MMM d, yyyy 'at' HH:mm", CultureInfo.InvariantCulture)}";
            if (!string.IsNullOrEmpty(project.EngineVersion)) tip += $" with Spot {project.EngineVersion}";
        }
        if (!IsCurrentVersion(project))
        {
            tip += $"\nThis editor is Spot {Application.Instance.EngineVersion}";
        }
        return tip + "\nDouble-click or press Enter to open";
    }

    // ----- Card menu ---------------------------------------------------------------------------------------

    private void RequestMenu(string path, Vector2 anchor, Vector2 pivot)
    {
        _menuPath = path;
        _menuAnchor = anchor;
        _menuPivot = pivot;
        _menuRequested = true;
    }

    private void DrawProjectMenu()
    {
        if (_menuRequested)
        {
            ImGui.OpenPopup(ProjectMenuId);
            ImGui.SetNextWindowPos(_menuAnchor, ImGuiCond.Always, _menuPivot);
            _menuRequested = false;
        }

        PushMenuStyle();
        if (ImGui.BeginPopup(ProjectMenuId))
        {
            string path = _menuPath ?? string.Empty;
            if (ImGui.MenuItem($"{EditorIcons.FolderOpen}  Open", "Enter")) _pendingOpen = path;
            if (ImGui.MenuItem($"{EditorIcons.Folder}  {RevealLabel}")) RevealInFileManager(path);
            if (ImGui.MenuItem($"{EditorIcons.Copy}  Copy Path", "Ctrl+C")) ImGui.SetClipboardText(path);
            ImGui.Separator();
            if (ImGui.MenuItem($"{EditorIcons.Trash}  Remove from List", "Del")) _pendingRemove = path;
            ImGui.EndPopup();
        }
        PopMenuStyle();
    }

    // ----- Empty states ------------------------------------------------------------------------------------

    private void DrawEmptyState(in LauncherColors c, float width, float height)
    {
        var art = new Vector2(176, 99);
        var buttonSize = new Vector2(148, LauncherLayout.ButtonHeight);
        float blockHeight = art.Y + Space.Xl + EditorFonts.Title.FontSize + Space.Sm + EditorFonts.Body.FontSize +
            Space.Xl + buttonSize.Y;
        Vector2 origin = ImGui.GetCursorScreenPos();
        float top = origin.Y + MathF.Max(0, (height - blockHeight) * 0.42f);
        float centerX = origin.X + width * 0.5f;
        var dl = ImGui.GetWindowDrawList();

        Vector2 artMin = new(MathF.Round(centerX - art.X * 0.5f), MathF.Round(top));
        ProjectPlaceholder.Draw(dl, "Spot", artMin, artMin + art, LauncherLayout.CardRounding, ImDrawFlags.None, saturation: 0.0f);
        dl.AddRect(artMin + new Vector2(0.5f), artMin + art - new Vector2(0.5f), U32(c.CardBorderHovered), LauncherLayout.CardRounding);

        float y = artMin.Y + art.Y + Space.Xl;
        y = CenteredLine(dl, EditorFonts.Title, "No projects yet", centerX, y, c.Text) + Space.Sm;
        y = CenteredLine(dl, EditorFonts.Body, "Create a new project or open an existing one to get started.", centerX, y, c.TextMuted) + Space.Xl;

        ImGui.SetCursorScreenPos(new Vector2(MathF.Round(centerX - buttonSize.X - Space.Xs), y));
        if (Button("##emptyNew", EditorIcons.Plus, "New Project", buttonSize, ButtonKind.Primary, c, "Ctrl+N")) OpenNewProjectDialog();
        ImGui.SetCursorScreenPos(new Vector2(MathF.Round(centerX + Space.Xs), y));
        if (Button("##emptyOpen", EditorIcons.FolderOpen, "Open Project", buttonSize, ButtonKind.Secondary, c, "Ctrl+O")) _pendingBrowse = true;
    }

    private void DrawNoMatches(in LauncherColors c, float width, float height)
    {
        Vector2 origin = ImGui.GetCursorScreenPos();
        float centerX = origin.X + width * 0.5f;
        var dl = ImGui.GetWindowDrawList();
        float y = origin.Y + MathF.Max(0, height * 0.28f);

        y = CenteredLine(dl, EditorFonts.Title, "No matching projects", centerX, y, c.Text) + Space.Sm;
        string query = EllipsizeEnd(EditorFonts.Body, _search.Trim(), width * 0.6f);
        y = CenteredLine(dl, EditorFonts.Body, $"Nothing matches \"{query}\".", centerX, y, c.TextMuted) + Space.Lg;

        var size = new Vector2(124, LauncherLayout.ControlHeight);
        ImGui.SetCursorScreenPos(new Vector2(MathF.Round(centerX - size.X * 0.5f), y));
        if (Button("##clearSearch", null, "Clear search", size, ButtonKind.Secondary, c, "Esc")) _search = string.Empty;
    }

    // Draws one centered line and returns the y just below it.
    private static float CenteredLine(ImDrawListPtr dl, ImFontPtr font, string text, float centerX, float y, Vector4 color)
    {
        Text(dl, font, new Vector2(centerX - Measure(font, text).X * 0.5f, y), color, text);
        return y + font.FontSize;
    }

    // ----- Formatting --------------------------------------------------------------------------------------

    /// <summary>A short "time ago" phrase ("2 hours ago", "yesterday", or a date), or null when unknown.</summary>
    private static string? RelativeTime(DateTime? utc)
    {
        if (utc == null) return null;

        TimeSpan span = DateTime.UtcNow - utc.Value;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return Plural((int)span.TotalMinutes, "minute");
        if (span.TotalHours < 24) return Plural((int)span.TotalHours, "hour");
        if (span.TotalDays < 2) return "yesterday";
        if (span.TotalDays < 7) return Plural((int)span.TotalDays, "day");
        if (span.TotalDays < 30) return Plural((int)(span.TotalDays / 7), "week");
        return utc.Value.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

        static string Plural(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";
    }

    private static string Capitalize(string text) =>
        text.Length > 0 && char.IsLower(text[0]) ? char.ToUpperInvariant(text[0]) + text[1..] : text;
}
