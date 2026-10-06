using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.Engine.Assets;
using Spot.Engine;
using Spot.Engine.Scenes;
using Spot.Engine.Audio;
using Spot.Engine.Graphics;
using Spot.DebugUI.UI;
using Spot.Editor.UI;
using Spot.Build;

namespace Spot.Editor.Panels;

public class AssetBrowserPanel
{
    private enum AssetKind { Folder, Script, Scene, Image, Model, Material, Prefab, Audio, Controller, UIDocument, Other }

    private readonly struct AssetEntry
    {
        public readonly string FullPath;
        public readonly string Name;
        public readonly bool IsDirectory;
        public readonly AssetKind Kind;
        // Only meaningful for directories: whether the folder contains anything (drives the empty/full icon).
        public readonly bool HasContents;

        public AssetEntry(string fullPath, string name, bool isDirectory, AssetKind kind, bool hasContents = false)
        {
            FullPath = fullPath;
            Name = name;
            IsDirectory = isDirectory;
            Kind = kind;
            HasContents = hasContents;
        }
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };
    private static readonly string[] ModelExtensions = { ".obj", ".fbx", ".gltf", ".glb", ".dae", ".ply", ".stl" };
    private static readonly string[] AudioExtensions = { ".wav", ".ogg" };
    private const int MaxThumbnails = 128;

    // The engine's built-in assets appear as a read-only virtual folder at the project root: "builtin:" lists one
    // subfolder per kind ("builtin:Mesh/", ...), and each entry's path is its builtin: reference.
    private const string BuiltinRoot = BuiltinAssets.Scheme;
    private static readonly (string Path, string Name, BuiltinAssetKind Kind)[] BuiltinFolders =
    {
        (BuiltinRoot + "Mesh/", "Meshes", BuiltinAssetKind.Mesh),
        (BuiltinRoot + "Texture/", "Textures", BuiltinAssetKind.Texture),
        (BuiltinRoot + "Material/", "Materials", BuiltinAssetKind.Material),
    };

    private readonly EditorContext _context;
    private string _currentDirectory;
    private string _baseDirectory;

    // The last real project folder visited: where copies of built-in assets go.
    private string _lastProjectDirectory;

    private string _searchQuery = "";
    private float _iconSize = 84.0f;
    private string? _selectedPath;
    private string? _pendingNavigate;

    // Multi-selection: every selected asset path (_selectedPath is the primary, i.e. the last one clicked).
    // _rangeAnchorPath is the entry a Shift+click range extends from (the last plain/Ctrl click).
    private readonly List<string> _selectedPaths = new();
    private string? _rangeAnchorPath;

    // A plain click inside a multi-selection is deferred: it collapses the selection to this asset on mouse
    // release, but only if no drag started in between — so the whole selection can be dragged as a group.
    private string? _pendingClickPath;

    // Full path of the asset currently being dragged, recorded when a drag starts so a folder drop target
    // can move it without reparsing the kind-specific payload.
    private string? _dragPath;

    // Asset clipboard, shared across panel instances. A cut pastes as a move (carrying the .meta guid); a
    // copy pastes as a fresh asset (no .meta, so a new guid is minted on the next scan).
    private static string? s_clipboardPath;
    private static bool s_clipboardCut;

    // Inline rename state: set after creation or explicit rename; rendered in the tile instead of the label.
    private string? _inlineRenamePath;
    private string _inlineRenameBuffer = "";
    private bool _inlineRenameFocusPending;
    private bool _inlineRenameIsNew;

    // Deferred deletion (still a confirmation modal). Holds one or more targets for a multi-selection delete.
    private bool _isDeleting;
    private readonly List<string> _deleteTargets = new();

    // Thumbnail cache for the current directory (disposed when the directory changes).
    private readonly Dictionary<string, Texture2D> _thumbnails = new();
    private readonly HashSet<string> _thumbFailed = new();
    private readonly Dictionary<string, Spot.Engine.Graphics.Framebuffer> _materialPreviews = new();
    private readonly Dictionary<string, Spot.Engine.Graphics.Framebuffer> _modelPreviews = new();
    private readonly HashSet<string> _modelFailed = new();

    // Rendering a model preview costs a load + offscreen draw; cap how many first-time renders happen per
    // frame so opening a folder full of models spreads the work over a few frames instead of hitching.
    private const int MaxModelPreviewsPerFrame = 2;
    private int _modelPreviewsThisFrame;

    // Directory-listing cache. GatherEntries does filesystem I/O (enumerate + sort + a per-subfolder
    // "has contents" probe), which previously ran every frame on the same folder. We reuse the last scan
    // until the folder or search text changes (cache key), an in-panel mutation invalidates it, or a short
    // refresh window elapses — so a file created by an external tool or the cook pipeline still shows up
    // promptly without a per-frame scan.
    private List<AssetEntry>? _entriesCache;
    private string? _entriesCacheDir;
    private string? _entriesCacheQuery;
    private double _entriesCacheTime;
    private const double EntriesRefreshSeconds = 0.5;

    public Action<string>? OnAssetOpened;

    public AssetBrowserPanel(EditorContext context)
    {
        _context = context;
        _baseDirectory = Spot.Build.Project.Active?.GetAssetDirectory() ?? Environment.CurrentDirectory;
        EnsureDirectory(_baseDirectory);
        _currentDirectory = _baseDirectory;
        _lastProjectDirectory = _baseDirectory;
    }

    private static bool IsBuiltinPath(string? path) =>
        path is not null && path.StartsWith(BuiltinRoot, StringComparison.OrdinalIgnoreCase);

    private bool InBuiltin => IsBuiltinPath(_currentDirectory);

    public string CurrentDirectory => _currentDirectory;

    public void OnImGuiRender(bool asWindow = false)
    {
        // Track project changes and reset to its asset directory.
        var currentProjectAssetDir = Spot.Build.Project.Active?.GetAssetDirectory() ?? Environment.CurrentDirectory;
        if (_baseDirectory != currentProjectAssetDir)
        {
            _baseDirectory = currentProjectAssetDir;
            EnsureDirectory(_baseDirectory);
            SetDirectory(_baseDirectory);
            _lastProjectDirectory = _baseDirectory;
        }

        if (asWindow)
        {
            ImGui.Begin("Asset Browser");
        }

        _pendingNavigate = null;
        _modelPreviewsThisFrame = 0;

        DrawToolbar();
        ImGui.Separator();

        float bottomHeight = ImGui.GetTextLineHeightWithSpacing();
        ImGui.BeginChild("AssetGrid", new Vector2(0, -bottomHeight), ImGuiChildFlags.None);
        DrawGrid();
        DrawEmptySpaceContextMenu();
        ImGui.EndChild();

        // The grid child is itself an item in the parent window, so dropping an entity anywhere over it turns
        // that entity into a .sptprefab asset in the current folder.
        AcceptEntityDropToCreatePrefab();

        string statusText = _selectedPath != null ? $"Selected: {Path.GetFileName(_selectedPath)}" : " ";
        ImGui.TextDisabled(statusText);

        HandleModals();

        // Apply a deferred navigation once all widgets for the frame have been submitted.
        if (_pendingNavigate != null)
        {
            SetDirectory(_pendingNavigate);
        }

        if (asWindow)
        {
            ImGui.End();
        }
    }

    private void DrawToolbar()
    {
        var palette = EditorThemeManager.Current.Palette;

        // Breadcrumb: clickable path segments from the project's asset root.
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, palette.FrameBgHovered);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3));

        if (ImGui.Button($"{EditorIcons.FolderOpen}  {BaseName(_baseDirectory)}"))
        {
            _pendingNavigate = _baseDirectory;
        }
        if (ImGui.BeginDragDropTarget())
        {
            if (TryAcceptAssetMove()) MoveDraggedInto(_baseDirectory);
            ImGui.EndDragDropTarget();
        }

        if (InBuiltin)
        {
            ImGui.SameLine(0, 2);
            ImGui.TextDisabled(">");
            ImGui.SameLine(0, 2);
            if (ImGui.Button("Built-in##crumb"))
            {
                _pendingNavigate = BuiltinRoot;
            }

            foreach ((string path, string name, _) in BuiltinFolders)
            {
                if (string.Equals(_currentDirectory, path, StringComparison.OrdinalIgnoreCase))
                {
                    ImGui.SameLine(0, 2);
                    ImGui.TextDisabled(">");
                    ImGui.SameLine(0, 2);
                    ImGui.Button(name + "##crumb");
                }
            }
        }

        string rel = InBuiltin ? "." : Path.GetRelativePath(_baseDirectory, _currentDirectory);
        if (rel != ".")
        {
            string accum = _baseDirectory;
            foreach (var part in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                accum = Path.Combine(accum, part);
                ImGui.SameLine(0, 2);
                ImGui.TextDisabled(">");
                ImGui.SameLine(0, 2);
                if (ImGui.Button(part + "##crumb"))
                {
                    _pendingNavigate = accum;
                }
                if (ImGui.BeginDragDropTarget())
                {
                    if (TryAcceptAssetMove()) MoveDraggedInto(accum);
                    ImGui.EndDragDropTarget();
                }
            }
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);

        // Right-aligned search box (with a leading magnifier glyph) and thumbnail-size slider.
        const float sliderWidth = 90.0f;
        const float searchWidth = 200.0f;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float glyphWidth = ImGui.CalcTextSize(EditorIcons.Search).X;
        float rightGroup = glyphWidth + 6.0f + searchWidth + spacing + sliderWidth + 24.0f;
        
        ImGui.SameLine();
        float avail = ImGui.GetContentRegionAvail().X;
        if (avail > rightGroup)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - rightGroup));
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(EditorIcons.Search);
        ImGui.SameLine(0, 6.0f);
        ImGui.SetNextItemWidth(searchWidth);
        ImGui.InputTextWithHint("##assetsearch", "Search...", ref _searchQuery, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(sliderWidth);
        ImGui.SliderFloat("##iconsize", ref _iconSize, 48.0f, 128.0f, "");
    }

    private void DrawGrid()
    {
        List<AssetEntry> entries;
        try
        {
            entries = GetEntries();
        }
        catch (Exception ex)
        {
            ImGui.TextColored(EditorThemeManager.Current.Palette.LogError, $"Error reading directory: {ex.Message}");
            return;
        }

        if (entries.Count == 0)
        {
            ImGui.TextDisabled(string.IsNullOrEmpty(_searchQuery) ? "This folder is empty." : "No matching assets.");
            return;
        }

        float pad = 10.0f;
        float cellW = _iconSize + pad * 2;
        float cellH = TileTextTop(pad) + ImGui.GetTextLineHeight() + TileCaptionGap + EditorFonts.Small.FontSize + pad - 2;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float availW = ImGui.GetContentRegionAvail().X;
        int columns = Math.Max(1, (int)((availW + spacing) / (cellW + spacing)));

        for (int i = 0; i < entries.Count; i++)
        {
            if (i % columns != 0)
            {
                ImGui.SameLine();
            }
            DrawTile(entries, i, cellW, cellH, pad);
        }

        // Clicking empty space clears the selection — unless Ctrl/Shift is held, which keeps extending it.
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered()
            && !ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift)
        {
            ClearSelection();
            _context.SelectedAssetPath = null;
        }

        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && _inlineRenamePath == null && !ImGui.IsAnyItemActive())
        {
            bool hasSelection = _selectedPath != null;
            bool builtinSelected = IsBuiltinPath(_selectedPath);
            if (ImGui.GetIO().KeyCtrl)
            {
                // Copy/cut/duplicate act on the primary selection; multi-asset clipboard isn't supported yet.
                // A built-in can be copied (and pasted into a project folder) but not cut; duplicating one saves an
                // editable copy into the project.
                if (hasSelection && ImGui.IsKeyPressed(ImGuiKey.C)) CopySelected(cut: false);
                else if (hasSelection && !builtinSelected && ImGui.IsKeyPressed(ImGuiKey.X)) CopySelected(cut: true);
                else if (hasSelection && ImGui.IsKeyPressed(ImGuiKey.D))
                {
                    if (builtinSelected) CopyBuiltinsInto(_lastProjectDirectory);
                    else DuplicateAsset(_selectedPath!);
                }
                else if (!InBuiltin && ImGui.IsKeyPressed(ImGuiKey.V)) PasteClipboardInto(_currentDirectory);
            }
            else if (hasSelection && builtinSelected)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.Enter) && !Directory.Exists(_selectedPath!) && !IsBuiltinFolder(_selectedPath!))
                {
                    _context.Selection = null;
                    _context.SelectedAssetPath = _selectedPath;
                }
            }
            else if (hasSelection)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.F2))
                {
                    bool isDir = Directory.Exists(_selectedPath);
                    string initial = isDir ? Path.GetFileName(_selectedPath)! : Path.GetFileNameWithoutExtension(_selectedPath!);
                    StartInlineRename(_selectedPath!, initial, isNew: false);
                }
                else if (ImGui.IsKeyPressed(ImGuiKey.Delete))
                {
                    RequestDeleteSelection();
                }
            }
        }
    }

    private void DrawTile(List<AssetEntry> entries, int index, float cellW, float cellH, float pad)
    {
        AssetEntry entry = entries[index];
        var palette = EditorThemeManager.Current.Palette;
        var drawList = ImGui.GetWindowDrawList();

        ImGui.PushID(entry.FullPath);
        Vector2 p0 = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("tile", new Vector2(cellW, cellH));

        bool hovered = ImGui.IsItemHovered();
        bool selected = _selectedPaths.Contains(entry.FullPath);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            HandleTileClick(entries, index);
        }

        // A deferred plain click inside a multi-selection collapses to this asset on release (when it wasn't
        // the start of a drag, which clears the pending click in the drag source below).
        if (_pendingClickPath == entry.FullPath && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            if (ImGui.IsItemHovered()) SelectSingle(entry.FullPath);
            _pendingClickPath = null;
        }
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (entry.IsDirectory)
            {
                _pendingNavigate = entry.FullPath;
            }
            else if (IsBuiltinPath(entry.FullPath))
            {
                // Built-ins open read-only in the Inspector, with a way to copy them into the project.
                _context.Selection = null;
                _context.SelectedAssetPath = entry.FullPath;
            }
            else if (entry.Kind == AssetKind.Material || entry.Kind == AssetKind.Prefab)
            {
                // Open the material/prefab in the Inspector for editing (mirrors how scenes open on double-click).
                _context.Selection = null;
                _context.SelectedAssetPath = entry.FullPath;
            }
            else if (entry.Kind == AssetKind.Scene || entry.Kind == AssetKind.Controller || entry.Kind == AssetKind.UIDocument)
            {
                if (OnAssetOpened != null) OnAssetOpened.Invoke(entry.FullPath);
                else OpenExternally(entry.FullPath);
            }
            else
            {
                // Anything else (scripts, images, models, ...) opens in the OS default app.
                OpenExternally(entry.FullPath);
            }
        }

        // Drag as a typed payload (consumed by the Inspector and by folder tiles for moving). Folders drag
        // too, so a whole folder can be dropped into another. _dragPath records the real source path so the
        // move target doesn't have to reparse the (kind-specific) payload data.
        bool virtualFolder = entry.IsDirectory && IsBuiltinPath(entry.FullPath);
        if (!virtualFolder && ImGui.BeginDragDropSource())
        {
            _dragPath = entry.FullPath;
            (string payloadType, string payloadData) = DragPayloadFor(entry);
            SetDragPayload(payloadType, payloadData);
            // Dragging one of several selected assets carries the whole selection; label reflects that. The
            // payload holds one path, so the scene drop targets read the rest from AssetSpawner.DraggedPaths.
            bool dragsSelection = _selectedPaths.Contains(entry.FullPath) && _selectedPaths.Count > 1;
            AssetSpawner.DraggedPaths = dragsSelection ? _selectedPaths.ToArray() : new[] { entry.FullPath };
            ImGui.Text(dragsSelection ? $"{_selectedPaths.Count} items" : entry.Name);
            _pendingClickPath = null; // this press became a drag, so don't collapse the selection on release
            ImGui.EndDragDropSource();
        }

        // Drop onto a folder tile to move the dragged asset (or the whole selection) into it.
        if (entry.IsDirectory && !virtualFolder && ImGui.BeginDragDropTarget())
        {
            if (TryAcceptAssetMove())
            {
                MoveDraggedInto(entry.FullPath);
            }
            ImGui.EndDragDropTarget();
        }

        DrawItemContextMenu(entry);

        // Backgrounds: files sit on a soft, barely lifted card; folders have none until hovered or selected.
        Vector4? card = selected ? WithAlpha(palette.Accent, 0.16f)
            : hovered ? new Vector4(1, 1, 1, 0.06f)
            : entry.IsDirectory ? null
            : new Vector4(1, 1, 1, 0.03f);
        if (card is Vector4 cardColor)
        {
            drawList.AddRectFilled(p0, p0 + new Vector2(cellW, cellH), ImGui.GetColorU32(cardColor), 6.0f);
        }
        if (selected)
        {
            drawList.AddRect(p0, p0 + new Vector2(cellW, cellH), ImGui.GetColorU32(WithAlpha(palette.Accent, 0.65f)),
                6.0f, ImDrawFlags.None, 1.0f);
        }

        Vector2 iconMin = p0 + new Vector2(pad, pad);
        DrawIcon(drawList, iconMin, _iconSize, entry);

        // Under the icon, a divider in the asset kind's color (fading out toward the tile's edges) and a caption naming
        // the type tell assets apart even when their previews look alike (a material sphere and a sphere model).
        // Folders need neither.
        float textTop = p0.Y + TileTextTop(pad);
        if (!entry.IsDirectory)
        {
            float dividerY = p0.Y + pad + _iconSize + TileDividerGap;
            Vector4 accent = KindColor(entry.Kind);
            uint on = ImGui.GetColorU32(WithAlpha(accent, 0.9f));
            uint off = ImGui.GetColorU32(WithAlpha(accent, 0.0f));
            float mid = p0.X + cellW * 0.5f;
            Vector2 lo = new(p0.X + 6, dividerY);
            Vector2 hi = new(p0.X + cellW - 6, dividerY + TileDividerThickness);
            drawList.AddRectFilledMultiColor(lo, new Vector2(mid, hi.Y), off, on, on, off);
            drawList.AddRectFilledMultiColor(new Vector2(mid, lo.Y), hi, on, off, off, on);

            ImFontPtr small = EditorFonts.Small;
            string caption = Ellipsize(TypeLabel(entry), cellW - 6, small, small.FontSize);
            Vector2 cs = small.CalcTextSizeA(small.FontSize, float.MaxValue, 0.0f, caption);
            Vector2 captionPos = new Vector2(p0.X + (cellW - cs.X) * 0.5f, textTop + ImGui.GetTextLineHeight() + TileCaptionGap);
            drawList.AddText(small, small.FontSize, captionPos, ImGui.GetColorU32(palette.TextDisabled), caption);
        }

        bool isInlineRenaming = _inlineRenamePath == entry.FullPath;

        if (isInlineRenaming)
        {
            // Draw an InputText in place of the static label, its text on the label's line.
            Vector2 inputPos = new Vector2(p0.X + 4, textTop - ImGui.GetStyle().FramePadding.Y);
            ImGui.SetCursorScreenPos(inputPos);
            ImGui.SetNextItemWidth(cellW - 8);
            bool focusThisFrame = _inlineRenameFocusPending;
            if (_inlineRenameFocusPending)
            {
                ImGui.SetKeyboardFocusHere();
                _inlineRenameFocusPending = false;
            }
            bool submitted = ImGui.InputText("##tilename", ref _inlineRenameBuffer, 200,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            bool escaped = ImGui.IsKeyPressed(ImGuiKey.Escape);
            bool lostFocus = !focusThisFrame && !ImGui.IsItemActive();

            if (submitted || (lostFocus && !ImGui.IsItemHovered()))
            {
                CommitInlineRename(entry);
            }
            else if (escaped)
            {
                if (_inlineRenameIsNew)
                    DeleteEntry(entry.FullPath);
                _inlineRenamePath = null;
            }
        }
        else
        {
            // Filename label, centered and truncated with an ellipsis (full name in tooltip).
            string label = Ellipsize(entry.Name, cellW - 6, ImGui.GetFont(), ImGui.GetFontSize());
            Vector2 ts = ImGui.CalcTextSize(label);
            Vector2 labelPos = new Vector2(p0.X + (cellW - ts.X) * 0.5f, textTop);
            drawList.AddText(labelPos, ImGui.GetColorU32(palette.Text), label);

            if (hovered)
            {
                ImGui.SetTooltip(BuiltinAssets.TryGet(entry.FullPath, out BuiltinAsset builtin)
                    ? $"{builtin.Name}\n{builtin.Description}"
                    : entry.Name);
            }
        }

        ImGui.PopID();
    }

    // Tile layout below the icon box: the divider, then the name and the type caption.
    private const float TileDividerGap = 4.0f;
    private const float TileDividerThickness = 2.0f;
    private const float TileCaptionGap = 1.0f;

    // Offset of the name line from the top of a tile.
    private float TileTextTop(float pad) => pad + _iconSize + TileDividerGap + TileDividerThickness + 4.0f;

    // Truncates text with an ellipsis so it fits maxWidth when drawn with the given font and size.
    private static string Ellipsize(string text, float maxWidth, ImFontPtr font, float fontSize)
    {
        if (font.CalcTextSizeA(fontSize, float.MaxValue, 0.0f, text).X <= maxWidth)
        {
            return text;
        }

        float eWidth = font.CalcTextSizeA(fontSize, float.MaxValue, 0.0f, "...").X;
        for (int i = text.Length - 1; i > 0; i--)
        {
            if (font.CalcTextSizeA(fontSize, float.MaxValue, 0.0f, text.Substring(0, i)).X + eWidth <= maxWidth)
            {
                return text.Substring(0, i) + "...";
            }
        }
        return "...";
    }

    // The caption under a tile naming what kind of asset it is.
    private static string TypeLabel(AssetEntry entry) => entry.Kind switch
    {
        AssetKind.Folder => "Folder",
        AssetKind.Script => "C# Script",
        AssetKind.Scene => "Scene",
        AssetKind.Image => "Texture",
        AssetKind.Model => "Model",
        AssetKind.Material => "Material",
        AssetKind.Prefab => "Prefab",
        AssetKind.Audio => "Audio",
        AssetKind.Controller => "Animator",
        AssetKind.UIDocument => "UI Document",
        _ => Path.GetExtension(entry.Name) is { Length: > 1 } ext ? $"{ext[1..].ToUpperInvariant()} File" : "File",
    };

    // The asset kind's accent color, shared with its painted icon.
    private static Vector4 KindColor(AssetKind kind) => kind switch
    {
        AssetKind.Folder => AssetIcons.FolderAccent,
        AssetKind.Script => AssetIcons.ScriptAccent,
        AssetKind.Scene => AssetIcons.SceneAccent,
        AssetKind.Image => AssetIcons.ImageAccent,
        AssetKind.Model => AssetIcons.ModelAccent,
        AssetKind.Material => AssetIcons.MaterialAccent,
        AssetKind.Prefab => AssetIcons.PrefabAccent,
        AssetKind.Audio => AssetIcons.AudioAccent,
        AssetKind.Controller => AssetIcons.ControllerAccent,
        AssetKind.UIDocument => AssetIcons.UIAccent,
        _ => AssetIcons.FileAccent,
    };

    private static AudioClip? _previewClip;
    private static Voice _previewVoice;

    private static void PlayAudioPreview(string sourcePath)
    {
        try
        {
            StopAudioPreview();
            _previewClip = AudioClip.FromFile(sourcePath); // a source path decodes directly, no cooking needed
            _previewVoice = AudioManager.Play(_previewClip, spatial: false);
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Error("Failed to preview audio '{0}': {1}", sourcePath, ex.Message);
        }
    }

    private static void StopAudioPreview()
    {
        AudioManager.Stop(_previewVoice);
        _previewVoice = default;
        _previewClip?.Dispose();
        _previewClip = null;
    }

    private void DrawIcon(ImDrawListPtr drawList, Vector2 iconMin, float size, AssetEntry entry)
    {
        Vector2 iconMax = iconMin + new Vector2(size, size);

        // Dynamic previews take priority, drawn straight onto the tile with no backdrop: the image itself, or a
        // material/model rendered over a transparent background.
        if (entry.Kind == AssetKind.Image && TryGetBuiltinTexture(entry.FullPath, out Texture2D? builtinTex))
        {
            drawList.AddImage((IntPtr)builtinTex.Handle.Id, iconMin, iconMax, new Vector2(0, 1), new Vector2(1, 0));
            return;
        }

        if (entry.Kind == AssetKind.Image && TryGetThumbnail(entry.FullPath, out var tex))
        {
            float scale = Math.Min(size / tex.Width, size / tex.Height);
            float w = tex.Width * scale;
            float h = tex.Height * scale;
            Vector2 imgMin = iconMin + new Vector2((size - w) * 0.5f, (size - h) * 0.5f);
            drawList.AddImage((IntPtr)tex.Handle.Id, imgMin, imgMin + new Vector2(w, h), new Vector2(0, 1), new Vector2(1, 0));
            return;
        }

        if (entry.Kind == AssetKind.Material && TryGetMaterialPreview(entry.FullPath, out var matFb))
        {
            drawList.AddImage((IntPtr)matFb.ColorAttachment, iconMin, iconMax, new Vector2(0, 1), new Vector2(1, 0));
            return;
        }

        // Models render a live 3D thumbnail; while the model is still loading (or if it fails) we fall
        // through to the painted cube below.
        if (entry.Kind == AssetKind.Model && TryGetModelPreview(entry.FullPath, out var mdlFb))
        {
            drawList.AddImage((IntPtr)mdlFb.ColorAttachment, iconMin, iconMax, new Vector2(0, 1), new Vector2(1, 0));
            return;
        }

        // Everything else is a painted icon (see AssetIcons).
        ImFontPtr labelFont = EditorFonts.IconText;
        switch (entry.Kind)
        {
            case AssetKind.Folder: AssetIcons.Folder(drawList, iconMin, size, entry.HasContents); break;
            case AssetKind.Script: AssetIcons.Script(drawList, iconMin, size, labelFont); break;
            case AssetKind.Scene: AssetIcons.Scene(drawList, iconMin, size); break;
            case AssetKind.Image: AssetIcons.Image(drawList, iconMin, size); break;
            case AssetKind.Model: AssetIcons.Model(drawList, iconMin, size); break;
            case AssetKind.Material: AssetIcons.Material(drawList, iconMin, size); break;
            case AssetKind.Prefab: AssetIcons.Prefab(drawList, iconMin, size); break;
            case AssetKind.Audio: AssetIcons.Audio(drawList, iconMin, size); break;
            case AssetKind.Controller: AssetIcons.AnimatorController(drawList, iconMin, size); break;
            case AssetKind.UIDocument: AssetIcons.UIDocument(drawList, iconMin, size, labelFont); break;
            default: AssetIcons.File(drawList, iconMin, size, labelFont, ExtensionBadge(entry.Name)); break;
        }
    }

    // A short extension (up to four letters or digits, e.g. "JSON") for the generic file icon's badge; null otherwise.
    private static string? ExtensionBadge(string name)
    {
        string ext = Path.GetExtension(name);
        if (ext.Length < 2 || ext.Length > 5)
        {
            return null;
        }

        string label = ext[1..].ToUpperInvariant();
        foreach (char ch in label)
        {
            if (!char.IsAsciiLetterOrDigit(ch))
            {
                return null;
            }
        }
        return label;
    }

    // "Add to Scene" for anything that stands for an entity (a prefab, a model, an image, an audio clip, a UI
    // document): adds it to the active scene, exactly as dropping it on the Hierarchy does. Acts on every selected
    // asset, so right-clicking within a multi-selection adds all of it.
    private void DrawAddToSceneItem(AssetEntry entry)
    {
        if (AssetSpawner.KindOf(entry.FullPath) == AssetSpawnKind.None) return;

        Scene? scene = _context.ActiveScene;
        if (ImGui.MenuItem("Add to Scene", "", false, scene != null) && scene != null)
        {
            IEnumerable<string> paths = _selectedPaths.Contains(entry.FullPath) ? _selectedPaths : new[] { entry.FullPath };
            List<Entity> spawned = AssetSpawner.SpawnAll(scene, paths);
            if (spawned.Count > 0)
            {
                _context.SetSelectedEntities(spawned);
                Spot.DebugUI.Undo.EditorHistory.RecordSceneEdit(scene, AssetSpawner.AddLabel(paths));
            }
        }
    }

    private void DrawItemContextMenu(AssetEntry entry)
    {
        if (!ImGui.BeginPopupContextItem("itemctx"))
        {
            return;
        }

        // Right-clicking an asset outside the current selection makes it the selection; right-clicking within
        // a multi-selection keeps the whole group so an action (e.g. Delete) applies to all of it.
        if (!_selectedPaths.Contains(entry.FullPath))
        {
            SelectSingle(entry.FullPath);
        }

        if (IsBuiltinPath(entry.FullPath))
        {
            DrawBuiltinContextMenu(entry);
            ImGui.EndPopup();
            return;
        }

        if (entry.Kind == AssetKind.Material && ImGui.MenuItem("Edit Material"))
        {
            _context.Selection = null;
            _context.SelectedAssetPath = entry.FullPath;
        }

        DrawAddToSceneItem(entry);

        if (entry.Kind == AssetKind.Model && ImGui.MenuItem("Extract Materials (Embedded)"))
        {
            Spot.Engine.Assets.ModelMaterials.ExtractEmbedded(entry.FullPath);
        }

        if (entry.Kind == AssetKind.Audio)
        {
            if (ImGui.MenuItem($"{EditorIcons.Play}  Play")) PlayAudioPreview(entry.FullPath);
            if (ImGui.MenuItem($"{EditorIcons.Stop}  Stop")) StopAudioPreview();
        }

        if (entry.IsDirectory)
        {
            if (ImGui.MenuItem("Open")) _pendingNavigate = entry.FullPath;
        }
        else if (entry.Kind == AssetKind.Scene)
        {
            if (ImGui.MenuItem("Open Scene"))
            {
                if (OnAssetOpened != null) OnAssetOpened.Invoke(entry.FullPath);
                else OpenExternally(entry.FullPath);
            }
        }
        else if (entry.Kind == AssetKind.Controller)
        {
            if (ImGui.MenuItem("Edit Animator Controller"))
            {
                if (OnAssetOpened != null) OnAssetOpened.Invoke(entry.FullPath);
                else OpenExternally(entry.FullPath);
            }
        }
        else if (entry.Kind == AssetKind.UIDocument)
        {
            if (ImGui.MenuItem("Edit UI"))
            {
                if (OnAssetOpened != null) OnAssetOpened.Invoke(entry.FullPath);
                else OpenExternally(entry.FullPath);
            }
        }
        else
        {
            if (ImGui.MenuItem("Open Externally")) OpenExternally(entry.FullPath);
        }
        if (ImGui.MenuItem("Show in File Manager"))
        {
            RevealInExplorer(entry.FullPath);
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Copy", "Ctrl+C")) { _selectedPath = entry.FullPath; CopySelected(cut: false); }
        if (ImGui.MenuItem("Cut", "Ctrl+X")) { _selectedPath = entry.FullPath; CopySelected(cut: true); }
        if (ImGui.MenuItem("Duplicate", "Ctrl+D")) DuplicateAsset(entry.FullPath);
        if (ImGui.MenuItem("Paste", "Ctrl+V", false, s_clipboardPath != null))
        {
            // Paste into this folder when the target is a directory, else into the current folder.
            PasteClipboardInto(entry.IsDirectory ? entry.FullPath : _currentDirectory);
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Rename"))
        {
            bool isDir = entry.IsDirectory;
            string initial = isDir ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
            StartInlineRename(entry.FullPath, initial);
        }
        bool multi = _selectedPaths.Count > 1;
        if (ImGui.MenuItem(multi ? $"Delete {_selectedPaths.Count} Items" : "Delete"))
        {
            RequestDeleteSelection();
        }

        ImGui.EndPopup();
    }

    private void DrawEmptySpaceContextMenu()
    {
        if (!ImGui.BeginPopupContextWindow("AssetBrowserContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            return;
        }

        if (InBuiltin)
        {
            ImGui.TextDisabled("Built-in assets are read-only.");
            if (ImGui.MenuItem("Back to Assets"))
            {
                _pendingNavigate = _lastProjectDirectory;
            }
            ImGui.EndPopup();
            return;
        }

        if (ImGui.MenuItem("New Folder"))
        {
            string path = UniqueAssetPath(_currentDirectory, "New Folder", "");
            try { Directory.CreateDirectory(path); } catch { }
            StartInlineRename(path, Path.GetFileName(path), isNew: true);
        }
        if (ImGui.MenuItem("New Component Script"))
        {
            string path = UniqueAssetPath(_currentDirectory, "NewComponent", ".cs");
            CreateScript(Path.GetFileName(path));
            StartInlineRename(path, Path.GetFileNameWithoutExtension(path), isNew: true);
        }
        if (ImGui.MenuItem("New Scene"))
        {
            string path = UniqueAssetPath(_currentDirectory, "NewScene", ".sptscene");
            CreateScene(Path.GetFileName(path));
            StartInlineRename(path, Path.GetFileNameWithoutExtension(path), isNew: true);
        }
        if (ImGui.MenuItem("New Material"))
        {
            string path = UniqueAssetPath(_currentDirectory, "NewMaterial", ".sptmat");
            CreateMaterial(Path.GetFileName(path));
            StartInlineRename(path, Path.GetFileNameWithoutExtension(path), isNew: true);
        }
        if (ImGui.MenuItem("New Animator Controller"))
        {
            string path = UniqueAssetPath(_currentDirectory, "NewController", ".sptcontroller");
            CreateAnimatorController(Path.GetFileName(path));
            StartInlineRename(path, Path.GetFileNameWithoutExtension(path), isNew: true);
        }
        if (ImGui.MenuItem("New UI Document"))
        {
            string path = UniqueAssetPath(_currentDirectory, "NewUI", ".sptui");
            CreateUIDocument(Path.GetFileName(path));
            StartInlineRename(path, Path.GetFileNameWithoutExtension(path), isNew: true);
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Paste", "Ctrl+V", false, s_clipboardPath != null))
        {
            PasteClipboardInto(_currentDirectory);
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Open in File Manager"))
        {
            OpenExternally(_currentDirectory);
        }
        ImGui.EndPopup();
    }

    // --- Multi-selection --------------------------------------------------------------------------------

    // Collapses the selection to a single asset (also the primary and the range anchor).
    private void SelectSingle(string path)
    {
        _selectedPath = path;
        _selectedPaths.Clear();
        _selectedPaths.Add(path);
        _rangeAnchorPath = path;
    }

    private void ClearSelection()
    {
        _selectedPath = null;
        _selectedPaths.Clear();
        _rangeAnchorPath = null;
        _pendingClickPath = null;
    }

    // Turns a click on a tile into a selection change, honoring Ctrl (toggle one) and Shift (range).
    private void HandleTileClick(List<AssetEntry> entries, int index)
    {
        var io = ImGui.GetIO();
        string path = entries[index].FullPath;
        if (io.KeyShift && _rangeAnchorPath != null)
        {
            SelectRange(entries, _rangeAnchorPath, path);
            // The anchor stays fixed so successive Shift+clicks grow/shrink the same range.
        }
        else if (io.KeyCtrl)
        {
            ToggleSelection(path);
            _rangeAnchorPath = path;
        }
        else if (_selectedPaths.Contains(path) && _selectedPaths.Count > 1)
        {
            // Defer collapsing so a drag starting from within the selection carries the whole group.
            _pendingClickPath = path;
        }
        else
        {
            SelectSingle(path);
        }
    }

    private void ToggleSelection(string path)
    {
        if (_selectedPaths.Remove(path))
        {
            _selectedPath = _selectedPaths.Count > 0 ? _selectedPaths[^1] : null;
        }
        else
        {
            _selectedPaths.Add(path);
            _selectedPath = path; // newly added asset becomes the primary selection
        }
    }

    // Selects every entry between the anchor and the clicked tile in the grid's display order.
    private void SelectRange(List<AssetEntry> entries, string anchorPath, string clickedPath)
    {
        int a = entries.FindIndex(e => e.FullPath == anchorPath);
        int b = entries.FindIndex(e => e.FullPath == clickedPath);
        if (a < 0 || b < 0)
        {
            SelectSingle(clickedPath);
            return;
        }
        if (a > b) (a, b) = (b, a);
        _selectedPaths.Clear();
        for (int i = a; i <= b; i++) _selectedPaths.Add(entries[i].FullPath);
        _selectedPath = clickedPath; // primary follows the cursor
    }

    // Fills the delete-confirmation target list from the current selection and opens the modal.
    private void RequestDeleteSelection()
    {
        _deleteTargets.Clear();
        if (_selectedPaths.Count > 0) _deleteTargets.AddRange(_selectedPaths.Where(p => !IsBuiltinPath(p)));
        else if (_selectedPath != null && !IsBuiltinPath(_selectedPath)) _deleteTargets.Add(_selectedPath);
        if (_deleteTargets.Count > 0) _isDeleting = true;
    }

    // Moves the dragged asset into destDir. When the dragged asset is part of a multi-selection, the whole
    // selection moves together.
    private void MoveDraggedInto(string destDir)
    {
        if (string.IsNullOrEmpty(_dragPath)) return;
        if (IsBuiltinPath(destDir)) return;
        if (IsBuiltinPath(_dragPath))
        {
            // Built-ins can't move; dropping them on a project folder saves editable copies there instead.
            CopyBuiltinsInto(destDir, _selectedPaths.Contains(_dragPath) ? _selectedPaths.ToList() : new List<string> { _dragPath });
            _dragPath = null;
            return;
        }

        if (_selectedPaths.Contains(_dragPath) && _selectedPaths.Count > 1)
        {
            foreach (string p in _selectedPaths.ToList()) MoveEntryInto(p, destDir);
            ClearSelection();
        }
        else
        {
            MoveEntryInto(_dragPath, destDir);
        }
    }

    private void StartInlineRename(string fullPath, string bufferInitial, bool isNew = false)
    {
        // Creating a new asset routes here right after the file is written, so re-scan to include it.
        if (isNew)
        {
            InvalidateEntries();
        }

        SelectSingle(fullPath);
        _inlineRenamePath = fullPath;
        _inlineRenameBuffer = bufferInitial;
        _inlineRenameFocusPending = true;
        _inlineRenameIsNew = isNew;
    }

    // Returns a path that doesn't conflict with existing files/folders by appending a counter.
    private static string UniqueAssetPath(string dir, string baseName, string ext)
    {
        string candidate = Path.Combine(dir, baseName + ext);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        for (int i = 1; i < 100; i++)
        {
            candidate = Path.Combine(dir, $"{baseName} {i}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return candidate;
    }

    private void HandleModals()
    {
        if (_isDeleting) ImGui.OpenPopup("Delete Asset");

        bool deleteOpen = true;
        if (ImGui.BeginPopupModal("Delete Asset", ref deleteOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted(_deleteTargets.Count == 1
                ? $"Delete '{Path.GetFileName(_deleteTargets[0])}'?"
                : $"Delete {_deleteTargets.Count} items?");
            if (_deleteTargets.Any(Directory.Exists))
            {
                ImGui.TextColored(EditorThemeManager.Current.Palette.LogError,
                    _deleteTargets.Count == 1
                        ? "This folder and all its contents will be removed."
                        : "Any folders and all their contents will be removed.");
            }
            ImGui.Spacing();
            if (ImGui.Button("Delete", new Vector2(120, 0)))
            {
                foreach (string target in _deleteTargets) DeleteEntry(target);
                ClearSelection();
                _isDeleting = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                _isDeleting = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        else if (!deleteOpen)
        {
            _isDeleting = false;
        }
    }

    // Returns the current directory's entries, reusing the last scan when nothing that affects it has
    // changed. Navigation and search changes miss the cache key; in-panel create/delete/rename/paste call
    // InvalidateEntries; and the refresh window bounds how long an external change can go unseen.
    private List<AssetEntry> GetEntries()
    {
        double now = Spot.Engine.Application.Instance.Time;
        if (_entriesCache != null
            && _entriesCacheDir == _currentDirectory
            && _entriesCacheQuery == _searchQuery
            && now - _entriesCacheTime < EntriesRefreshSeconds)
        {
            return _entriesCache;
        }

        _entriesCache = GatherEntries();
        _entriesCacheDir = _currentDirectory;
        _entriesCacheQuery = _searchQuery;
        _entriesCacheTime = now;
        return _entriesCache;
    }

    // Forces the next GetEntries to re-scan, so an in-panel change shows immediately instead of waiting out
    // the refresh window.
    private void InvalidateEntries() => _entriesCache = null;

    private List<AssetEntry> GatherEntries()
    {
        if (InBuiltin)
        {
            return GatherBuiltinEntries();
        }

        var result = new List<AssetEntry>();

        // The read-only Built-in folder leads the project root.
        if (_currentDirectory == _baseDirectory
            && (string.IsNullOrEmpty(_searchQuery) || "Built-in".Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new AssetEntry(BuiltinRoot, "Built-in", true, AssetKind.Folder, hasContents: true));
        }

        var dirInfo = new DirectoryInfo(_currentDirectory);
        if (!dirInfo.Exists)
        {
            return result;
        }

        bool Matches(string name) =>
            string.IsNullOrEmpty(_searchQuery) || name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase);

        foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (Matches(dir.Name))
            {
                result.Add(new AssetEntry(dir.FullName, dir.Name, true, AssetKind.Folder, DirectoryHasContents(dir)));
            }
        }
        foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            // .meta sidecars are pipeline bookkeeping that lives beside each source; never show them.
            if (file.Name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Matches(file.Name))
            {
                result.Add(new AssetEntry(file.FullName, file.Name, false, Classify(file.Name)));
            }
        }
        return result;
    }

    // The Built-in folder: one subfolder per kind at its root (or, while searching, every matching asset), and
    // a kind's assets inside its subfolder.
    private List<AssetEntry> GatherBuiltinEntries()
    {
        var result = new List<AssetEntry>();
        bool Matches(string name) =>
            string.IsNullOrEmpty(_searchQuery) || name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase);

        bool atRoot = string.Equals(_currentDirectory, BuiltinRoot, StringComparison.OrdinalIgnoreCase);
        if (atRoot && string.IsNullOrEmpty(_searchQuery))
        {
            foreach ((string path, string name, _) in BuiltinFolders)
            {
                result.Add(new AssetEntry(path, name, true, AssetKind.Folder, hasContents: true));
            }

            return result;
        }

        foreach ((string path, _, BuiltinAssetKind kind) in BuiltinFolders)
        {
            if (!atRoot && !string.Equals(_currentDirectory, path, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (BuiltinAsset asset in BuiltinAssets.OfKind(kind).Where(a => Matches(a.Name)))
            {
                AssetKind assetKind = kind switch
                {
                    BuiltinAssetKind.Mesh => AssetKind.Model,
                    BuiltinAssetKind.Texture => AssetKind.Image,
                    _ => AssetKind.Material,
                };
                result.Add(new AssetEntry(asset.Reference, asset.Name, false, assetKind));
            }
        }

        return result;
    }

    private static bool IsBuiltinFolder(string path) =>
        string.Equals(path, BuiltinRoot, StringComparison.OrdinalIgnoreCase)
        || BuiltinFolders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

    // A built-in texture is drawn straight from its shared instance rather than loaded as a file thumbnail.
    private static bool TryGetBuiltinTexture(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? texture)
    {
        texture = null;
        if (!BuiltinAssets.TryGet(path, out BuiltinAsset asset) || asset.Kind != BuiltinAssetKind.Texture)
        {
            return false;
        }

        try
        {
            texture = BuiltinAssets.LoadTexture(asset.Reference);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // The context menu of a built-in asset or folder: no rename, delete or move — just ways to use or copy it.
    private void DrawBuiltinContextMenu(AssetEntry entry)
    {
        if (entry.IsDirectory)
        {
            if (ImGui.MenuItem("Open")) _pendingNavigate = entry.FullPath;
            return;
        }

        if (ImGui.MenuItem("Show in Inspector", "Enter"))
        {
            _context.Selection = null;
            _context.SelectedAssetPath = entry.FullPath;
        }

        DrawAddToSceneItem(entry);

        ImGui.Separator();
        string target = Path.GetRelativePath(Path.GetDirectoryName(_baseDirectory) ?? _baseDirectory, _lastProjectDirectory);
        if (ImGui.MenuItem($"Copy to Project ({target})", "Ctrl+D"))
        {
            CopyBuiltinsInto(_lastProjectDirectory);
        }

        if (ImGui.MenuItem("Copy", "Ctrl+C"))
        {
            _selectedPath = entry.FullPath;
            CopySelected(cut: false);
        }

        if (ImGui.MenuItem("Copy Reference"))
        {
            ImGui.SetClipboardText(entry.FullPath);
        }
    }

    // Saves editable copies of built-in assets (the selection by default) into a project folder.
    private void CopyBuiltinsInto(string destDir, IReadOnlyList<string>? references = null)
    {
        IEnumerable<string> sources = references ?? (_selectedPaths.Count > 0 ? _selectedPaths : new List<string> { _selectedPath ?? "" });
        foreach (string reference in sources.Where(r => IsBuiltinPath(r) && !IsBuiltinFolder(r)).ToList())
        {
            try
            {
                string path = BuiltinAssets.Export(reference, destDir);
                Spot.Engine.Log.Info("Copied built-in '{0}' to {1}.", BuiltinAssets.TryGet(reference, out BuiltinAsset a) ? a.Name : reference, path);
            }
            catch (Exception ex)
            {
                Spot.Engine.Log.Error("Failed to copy '{0}' into the project: {1}", reference, ex.Message);
            }
        }

        ClearThumbnails();
    }

    // Cheap "does this folder hold anything" probe for the empty/full folder icon. Enumeration stops at the
    // first entry; an unreadable directory is treated as empty rather than throwing.
    private static bool DirectoryHasContents(DirectoryInfo dir)
    {
        try { return dir.EnumerateFileSystemInfos().Any(); }
        catch { return false; }
    }

    private static AssetKind Classify(string name)
    {
        string ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext == ".cs") return AssetKind.Script;
        if (ext == ".sptscene") return AssetKind.Scene;
        if (ImageExtensions.Contains(ext)) return AssetKind.Image;
        if (ModelExtensions.Contains(ext)) return AssetKind.Model;
        if (AudioExtensions.Contains(ext)) return AssetKind.Audio;
        if (ext == ".sptmat") return AssetKind.Material;
        if (ext == ".sptprefab") return AssetKind.Prefab;
        if (ext == ".sptcontroller") return AssetKind.Controller;
        if (ext == ".sptui") return AssetKind.UIDocument;
        return AssetKind.Other;
    }

    private bool TryGetThumbnail(string path, out Texture2D texture)
    {
        if (_thumbnails.TryGetValue(path, out texture!))
        {
            return true;
        }
        if (_thumbFailed.Contains(path) || _thumbnails.Count >= MaxThumbnails)
        {
            return false;
        }

        try
        {
            texture = Texture2D.FromFile(path);
            _thumbnails[path] = texture;
            return true;
        }
        catch
        {
            _thumbFailed.Add(path);
            return false;
        }
    }

    private bool TryGetMaterialPreview(string path, out Spot.Engine.Graphics.Framebuffer fb)
    {
        if (_materialPreviews.TryGetValue(path, out fb!))
        {
            return true;
        }
        if (_materialPreviews.Count >= MaxThumbnails)
        {
            return false;
        }

        try
        {
            fb = new Spot.Engine.Graphics.Framebuffer(128, 128);
            var material = Spot.Engine.Assets.Material.Load(path);
            Spot.DebugUI.UI.MaterialPreviewHelper.RenderToFramebuffer(material, fb, transparentBackground: true);
            _materialPreviews[path] = fb;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetModelPreview(string path, out Spot.Engine.Graphics.Framebuffer fb)
    {
        if (_modelPreviews.TryGetValue(path, out fb!))
        {
            return true;
        }
        if (_modelFailed.Contains(path) || _modelPreviews.Count >= MaxThumbnails
            || _modelPreviewsThisFrame >= MaxModelPreviewsPerFrame)
        {
            return false;
        }

        try
        {
            // Non-blocking: returns null until the geometry is parsed and uploaded (pumped elsewhere each
            // frame). Show the glyph until then, and retry next frame.
            var model = Spot.Engine.Graphics.ModelImporter.RequestAsync(path);
            if (model is null)
            {
                return false;
            }

            _modelPreviewsThisFrame++;
            fb = new Spot.Engine.Graphics.Framebuffer(128, 128);
            Spot.DebugUI.UI.ModelPreviewHelper.RenderToFramebuffer(model, fb, transparentBackground: true);
            _modelPreviews[path] = fb;
            return true;
        }
        catch (Exception e)
        {
            _modelFailed.Add(path);
            Spot.Engine.Log.Warn("Failed to render model thumbnail for '{0}': {1}", path, e.Message);
            return false;
        }
    }

    private void CreateScript(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!name.EndsWith(".cs")) name += ".cs";
        EnsureDirectory(_currentDirectory);

        string filepath = Path.Combine(_currentDirectory, name);
        if (File.Exists(filepath)) return;

        string className = Path.GetFileNameWithoutExtension(name).Replace(" ", "");
        string template = Spot.DebugUI.UI.ComponentScripts.Template(className, Spot.DebugUI.UI.ComponentScripts.ProjectNamespace());
        File.WriteAllText(filepath, template);
    }

    private void CreateFolder(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        string path = Path.Combine(_currentDirectory, name.Trim());
        try { Directory.CreateDirectory(path); } catch { }
    }

    private void CreateScene(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!name.EndsWith(".sptscene")) name += ".sptscene";
        EnsureDirectory(_currentDirectory);
        string filepath = Path.Combine(_currentDirectory, name);
        if (File.Exists(filepath)) return;
        
        var newScene = new Spot.Engine.Scenes.Scene();
        new Spot.Engine.Scenes.SceneSerializer(newScene).Serialize(filepath);
    }

    private void CreateMaterial(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!name.EndsWith(".sptmat")) name += ".sptmat";
        EnsureDirectory(_currentDirectory);
        string filepath = Path.Combine(_currentDirectory, name);
        if (File.Exists(filepath)) return;

        new Spot.Engine.Assets.Material().Save(filepath);
    }

    private void CreateUIDocument(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!name.EndsWith(".sptui")) name += ".sptui";
        EnsureDirectory(_currentDirectory);
        string filepath = Path.Combine(_currentDirectory, name);
        if (File.Exists(filepath)) return;

        // Seed a minimal document: a single full-screen panel to drop widgets onto.
        var root = new Spot.Engine.UI.UIRoot();
        var panel = root.Panel();
        panel.Name = "Root";
        panel.Rect = new Spot.Engine.UI.UIRect
        {
            Anchor = System.Numerics.Vector2.Zero,
            Pivot = System.Numerics.Vector2.Zero,
            Position = System.Numerics.Vector2.Zero,
            Size = new System.Numerics.Vector2(1920f, 1080f),
        };
        Spot.Engine.UI.UISerializer.Save(root, filepath);
    }

    private void CreateAnimatorController(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!name.EndsWith(".sptcontroller")) name += ".sptcontroller";
        EnsureDirectory(_currentDirectory);
        string filepath = Path.Combine(_currentDirectory, name);
        if (File.Exists(filepath)) return;

        var controller = new Spot.Engine.Animation.AnimatorController();
        controller.States.Add(new Spot.Engine.Animation.AnimatorState { Name = "New State", EditorX = 220.0f, EditorY = 40.0f });
        controller.DefaultState = "New State";
        controller.Save(filepath);
    }

    // Accepts an entity dragged from the hierarchy onto the asset grid, writing it out as a .sptprefab in the
    // current folder and marking the source entity as an instance of the new prefab.
    private void AcceptEntityDropToCreatePrefab()
    {
        if (InBuiltin || !ImGui.BeginDragDropTarget())
        {
            return;
        }

        unsafe
        {
            var payload = ImGui.AcceptDragDropPayload("ENTITY");
            if (payload.NativePtr != null && _context.ActiveScene != null)
            {
                int entityId = *(int*)payload.Data;
                CreatePrefabFromEntity(new Entity(entityId, _context.ActiveScene));
            }
        }

        ImGui.EndDragDropTarget();
    }

    private void CreatePrefabFromEntity(Entity entity)
    {
        try
        {
            string path = UniqueAssetPath(_currentDirectory, SanitizeFileName(entity.Name), ".sptprefab");
            File.WriteAllText(path, Prefab.Serialize(entity));

            // Link the source entity to the new prefab so the hierarchy tints it as an instance.
            string? reference = Spot.Engine.Assets.AssetDatabase.ToGuidRef(path);
            entity.AddComponent(new PrefabInstance { PrefabRef = reference });

            SelectSingle(path);
            ClearThumbnails();
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Error("Failed to create prefab: {0}", ex.Message);
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "Prefab" : name;
    }

    private void CommitInlineRename(AssetEntry entry)
    {
        _inlineRenamePath = null;
        string trimmed = _inlineRenameBuffer.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        // Reattach the original extension for file assets (the buffer holds only the base name).
        string newName;
        if (entry.IsDirectory)
        {
            newName = trimmed;
        }
        else
        {
            string ext = Path.GetExtension(entry.Name);
            newName = trimmed.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ext;
        }

        if (newName != entry.Name)
        {
            bool isNewScript = _inlineRenameIsNew && entry.Kind == AssetKind.Script;
            string oldClassName = isNewScript ? Path.GetFileNameWithoutExtension(entry.Name).Replace(" ", "") : "";
            
            RenameEntry(entry.FullPath, newName);

            if (isNewScript)
            {
                string? dir = Path.GetDirectoryName(entry.FullPath);
                if (dir != null)
                {
                    string dest = Path.Combine(dir, newName);
                    if (File.Exists(dest))
                    {
                        try
                        {
                            string newClassName = Path.GetFileNameWithoutExtension(newName).Replace(" ", "");
                            string content = File.ReadAllText(dest);
                            content = content.Replace($"class {oldClassName}", $"class {newClassName}");
                            File.WriteAllText(dest, content);
                        }
                        catch (Exception ex)
                        {
                            Spot.Engine.Log.Error("Failed to update script class name: {0}", ex.Message);
                        }
                    }
                }
            }
        }
    }

    private void RenameEntry(string fullPath, string newName)
    {
        newName = newName?.Trim() ?? "";
        if (string.IsNullOrEmpty(newName)) return;

        string? parent = Path.GetDirectoryName(fullPath);
        if (parent == null) return;
        string dest = Path.Combine(parent, newName);
        if (dest == fullPath) return;

        try
        {
            if (Directory.Exists(fullPath)) Directory.Move(fullPath, dest);
            else if (File.Exists(fullPath)) File.Move(fullPath, dest);
            if (_selectedPath == fullPath) _selectedPath = dest;
            int selIdx = _selectedPaths.IndexOf(fullPath);
            if (selIdx >= 0) _selectedPaths[selIdx] = dest;
            if (_rangeAnchorPath == fullPath) _rangeAnchorPath = dest;
            if (_context.SelectedAssetPath == fullPath) _context.SelectedAssetPath = dest;
            InvalidateEntries();
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Error("Failed to rename asset: {0}", ex.Message);
        }
        ClearThumbnails();
    }

    // Payload types a folder tile accepts as a move. Mirrors the types produced by DragPayloadFor.
    private static readonly string[] MovablePayloads =
    {
        "FOLDER_FILE", "IMAGE_FILE", "MODEL_FILE", "MATERIAL_FILE",
        "SCENE_FILE", "PREFAB_FILE", "AUDIO_FILE", "SCRIPT_FILE", "CONTROLLER_FILE",
    };

    // The drag payload (type + data) for an entry. Data is the full path for everything the Inspector
    // resolves by path; scripts/other keep their historical name-only payload for the script slot consumer.
    private static (string Type, string Data) DragPayloadFor(AssetEntry entry) => entry.IsDirectory
        ? ("FOLDER_FILE", entry.FullPath)
        : entry.Kind switch
        {
            AssetKind.Image => ("IMAGE_FILE", entry.FullPath),
            AssetKind.Model => ("MODEL_FILE", entry.FullPath),
            AssetKind.Material => ("MATERIAL_FILE", entry.FullPath),
            AssetKind.Scene => ("SCENE_FILE", entry.FullPath),
            AssetKind.Prefab => ("PREFAB_FILE", entry.FullPath),
            AssetKind.Audio => ("AUDIO_FILE", entry.FullPath),
            AssetKind.Controller => ("CONTROLLER_FILE", entry.FullPath),
            AssetKind.UIDocument => ("UI_FILE", entry.FullPath),
            _ => ("SCRIPT_FILE", entry.Name),
        };

    private static unsafe void SetDragPayload(string type, string data)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(data + "\0");
        fixed (byte* p = bytes)
        {
            ImGui.SetDragDropPayload(type, (IntPtr)p, (uint)bytes.Length);
        }
    }

    // Returns true on the frame a movable payload is dropped on the current target. AcceptDragDropPayload
    // (without AcceptBeforeDelivery) only returns non-null once the mouse is released, so a non-null result
    // means "delivered here".
    private static bool TryAcceptAssetMove()
    {
        foreach (string type in MovablePayloads)
        {
            var payload = ImGui.AcceptDragDropPayload(type);
            unsafe
            {
                if (payload.NativePtr != null) return true;
            }
        }
        return false;
    }

    // Moves a file or folder into destDir, carrying its committed .meta sidecar so the asset keeps its guid
    // identity (and existing guid: scene references keep resolving). Never throws: bad moves log and continue.
    private void MoveEntryInto(string? sourcePath, string destDir)
    {
        if (string.IsNullOrEmpty(sourcePath)) return;

        try
        {
            // No-op if it already lives here.
            string? sourceParent = Path.GetDirectoryName(sourcePath);
            if (string.Equals(sourceParent, destDir, StringComparison.OrdinalIgnoreCase)) return;

            string srcFull = Path.GetFullPath(sourcePath);
            string dstFull = Path.GetFullPath(destDir);

            // Never move a folder into itself or one of its own descendants (would orphan the tree).
            if (Directory.Exists(sourcePath))
            {
                if (string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase)) return;
                if (dstFull.StartsWith(srcFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            }

            string name = Path.GetFileName(sourcePath);
            string dest = Path.Combine(destDir, name);
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                Spot.Engine.Log.Error("Cannot move '{0}': an item with that name already exists in the target folder.", name);
                return;
            }

            if (!MovePath(sourcePath, dest)) return;

            if (_selectedPath == sourcePath) _selectedPath = dest;
            int selIdx = _selectedPaths.IndexOf(sourcePath);
            if (selIdx >= 0) _selectedPaths[selIdx] = dest;
            if (_rangeAnchorPath == sourcePath) _rangeAnchorPath = dest;
            if (_context.SelectedAssetPath == sourcePath) _context.SelectedAssetPath = dest;
            ClearThumbnails();
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Error("Failed to move asset: {0}", ex.Message);
        }
        finally
        {
            _dragPath = null;
        }
    }


    private void CopySelected(bool cut)
    {
        if (_selectedPath == null) return;
        s_clipboardPath = _selectedPath;
        s_clipboardCut = cut;
    }

    // Copies an asset in place (same folder) under a unique name and selects the copy.
    private void DuplicateAsset(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (dir == null) return;

        var (baseName, ext) = SplitName(path);
        string dest = UniqueAssetPath(dir, baseName, ext);
        try
        {
            CopyPath(path, dest);
            SelectSingle(dest);
            ClearThumbnails();
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Error("Failed to duplicate asset: {0}", ex.Message);
        }
    }

    // Pastes the clipboard asset into destDir. A cut moves (carrying the .meta guid) and is then consumed; a
    // copy duplicates as a fresh asset. Names that collide in the target get a unique suffix.
    private void PasteClipboardInto(string destDir)
    {
        if (string.IsNullOrEmpty(s_clipboardPath) || IsBuiltinPath(destDir)) return;
        string src = s_clipboardPath;

        // A copied built-in pastes as an editable copy (and stays on the clipboard for more).
        if (IsBuiltinPath(src))
        {
            CopyBuiltinsInto(destDir, new List<string> { src });
            return;
        }

        if (!File.Exists(src) && !Directory.Exists(src))
        {
            s_clipboardPath = null; // stale (moved/deleted elsewhere)
            return;
        }

        try
        {
            // Never paste a folder into itself or one of its own descendants.
            if (Directory.Exists(src))
            {
                string srcFull = Path.GetFullPath(src);
                string dstFull = Path.GetFullPath(destDir);
                if (string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase)
                    || dstFull.StartsWith(srcFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            var (baseName, ext) = SplitName(src);
            string dest = UniqueAssetPath(destDir, baseName, ext);

            if (s_clipboardCut)
            {
                string? srcParent = Path.GetDirectoryName(src);
                // Moving into the same folder is a no-op; keep the clipboard so it can go elsewhere.
                if (!string.Equals(srcParent, destDir, StringComparison.OrdinalIgnoreCase))
                {
                    MovePath(src, dest);
                    SelectSingle(dest);
                    s_clipboardPath = null; // a cut is consumed by its paste
                }
            }
            else
            {
                CopyPath(src, dest);
                SelectSingle(dest);
            }
            ClearThumbnails();
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Error("Failed to paste asset: {0}", ex.Message);
        }
    }

    // Moves a file or folder to an exact destination path, carrying the .meta sidecar so the guid identity
    // survives. Returns false when the source no longer exists.
    private static bool MovePath(string src, string dest)
    {
        if (Directory.Exists(src))
        {
            Directory.Move(src, dest);
            return true;
        }
        if (File.Exists(src))
        {
            File.Move(src, dest);
            string meta = Spot.Engine.Assets.AssetMeta.MetaPathFor(src);
            if (File.Exists(meta)) File.Move(meta, Spot.Engine.Assets.AssetMeta.MetaPathFor(dest));
            return true;
        }
        return false;
    }

    // Copies a file or folder (recursively) to dest. The committed .meta sidecars are deliberately skipped:
    // a copy is a distinct asset and must mint its own guid on the next database scan, not clone the source's.
    private static void CopyPath(string src, string dest)
    {
        if (Directory.Exists(src))
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(src))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
            }
            foreach (string dir in Directory.GetDirectories(src))
            {
                CopyPath(dir, Path.Combine(dest, Path.GetFileName(dir)));
            }
        }
        else if (File.Exists(src))
        {
            File.Copy(src, dest);
        }
    }

    // Splits a path into the (baseName, extension) pair UniqueAssetPath expects: folders have no extension.
    private static (string BaseName, string Ext) SplitName(string path) =>
        Directory.Exists(path)
            ? (Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), "")
            : (Path.GetFileNameWithoutExtension(path), Path.GetExtension(path));

    private void DeleteEntry(string fullPath)
    {
        try
        {
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
            else if (File.Exists(fullPath)) File.Delete(fullPath);
            if (_selectedPath == fullPath) _selectedPath = null;
            _selectedPaths.Remove(fullPath);
            if (_rangeAnchorPath == fullPath) _rangeAnchorPath = null;
            if (_context.SelectedAssetPath == fullPath) _context.SelectedAssetPath = null;
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Error("Failed to delete asset: {0}", ex.Message);
        }
        ClearThumbnails();
    }

    private void SetDirectory(string path)
    {
        if (path == _currentDirectory)
        {
            return;
        }
        _currentDirectory = path;
        if (!IsBuiltinPath(path))
        {
            _lastProjectDirectory = path;
        }
        ClearSelection();
        ClearThumbnails();
    }

    private void ClearThumbnails()
    {
        // Directory contents changed (navigation, delete, and paste all route through here), so the cached
        // listing is stale too.
        InvalidateEntries();
        foreach (var tex in _thumbnails.Values)
        {
            tex.Dispose();
        }
        _thumbnails.Clear();
        _thumbFailed.Clear();

        foreach (var fb in _materialPreviews.Values)
        {
            fb.Dispose();
        }
        _materialPreviews.Clear();

        foreach (var fb in _modelPreviews.Values)
        {
            fb.Dispose();
        }
        _modelPreviews.Clear();
        _modelFailed.Clear();
    }

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            try { Directory.CreateDirectory(path); } catch { }
        }
    }

    private static string BaseName(string path)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? "Assets" : name;
    }

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

    private static void RevealInExplorer(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", $"-R \"{path}\"");
            }
            else if (OperatingSystem.IsLinux())
            {
                var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (dir != null)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true
                    });
                }
            }
        }
        catch { }
    }

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    // Shortens text with a trailing ellipsis so it fits within maxWidth pixels.
    private static string Truncate(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        for (int len = text.Length - 1; len > 0; len--)
        {
            string candidate = text.Substring(0, len) + ellipsis;
            if (ImGui.CalcTextSize(candidate).X <= maxWidth)
            {
                return candidate;
            }
        }
        return ellipsis;
    }
}
