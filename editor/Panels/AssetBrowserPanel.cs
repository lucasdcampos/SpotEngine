using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.Audio;
using Spot.Editor.UI;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Editor.Panels;

public class AssetBrowserPanel
{
    private enum AssetKind { Folder, Script, Scene, Image, Model, Material, Prefab, Audio, Other }

    private readonly struct AssetEntry
    {
        public readonly string FullPath;
        public readonly string Name;
        public readonly bool IsDirectory;
        public readonly AssetKind Kind;

        public AssetEntry(string fullPath, string name, bool isDirectory, AssetKind kind)
        {
            FullPath = fullPath;
            Name = name;
            IsDirectory = isDirectory;
            Kind = kind;
        }
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };
    private static readonly string[] ModelExtensions = { ".obj", ".fbx", ".gltf", ".glb", ".dae", ".ply", ".stl" };
    private static readonly string[] AudioExtensions = { ".wav", ".ogg" };
    private const int MaxThumbnails = 128;

    private readonly EditorContext _context;
    private string _currentDirectory;
    private string _baseDirectory;

    private string _searchQuery = "";
    private float _iconSize = 84.0f;
    private string? _selectedPath;
    private string? _pendingNavigate;

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

    // Deferred deletion (still a confirmation modal).
    private bool _isDeleting;
    private string _deleteTarget = "";

    // Thumbnail cache for the current directory (disposed when the directory changes).
    private readonly Dictionary<string, Texture2D> _thumbnails = new();
    private readonly HashSet<string> _thumbFailed = new();
    private readonly Dictionary<string, Spot.Rendering.Framebuffer> _materialPreviews = new();

    public Action<string>? OnAssetOpened;

    public AssetBrowserPanel(EditorContext context)
    {
        _context = context;
        _baseDirectory = Spot.Core.Project.Active?.GetAssetDirectory() ?? Environment.CurrentDirectory;
        EnsureDirectory(_baseDirectory);
        _currentDirectory = _baseDirectory;
    }

    public string CurrentDirectory => _currentDirectory;

    public void OnImGuiRender(bool asWindow = false)
    {
        // Track project changes and reset to its asset directory.
        var currentProjectAssetDir = Spot.Core.Project.Active?.GetAssetDirectory() ?? Environment.CurrentDirectory;
        if (_baseDirectory != currentProjectAssetDir)
        {
            _baseDirectory = currentProjectAssetDir;
            EnsureDirectory(_baseDirectory);
            SetDirectory(_baseDirectory);
        }

        if (asWindow)
        {
            ImGui.Begin("Asset Browser");
        }

        _pendingNavigate = null;

        DrawToolbar();
        DrawActionBar();
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
            if (TryAcceptAssetMove()) MoveEntryInto(_dragPath, _baseDirectory);
            ImGui.EndDragDropTarget();
        }

        string rel = Path.GetRelativePath(_baseDirectory, _currentDirectory);
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
                    if (TryAcceptAssetMove()) MoveEntryInto(_dragPath, accum);
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
            entries = GatherEntries();
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
        float cellH = _iconSize + pad * 2 + ImGui.GetTextLineHeight() + 4;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float availW = ImGui.GetContentRegionAvail().X;
        int columns = Math.Max(1, (int)((availW + spacing) / (cellW + spacing)));

        for (int i = 0; i < entries.Count; i++)
        {
            if (i % columns != 0)
            {
                ImGui.SameLine();
            }
            DrawTile(entries[i], cellW, cellH, pad);
        }

        // Clicking empty space clears the selection.
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered())
        {
            _selectedPath = null;
            _context.SelectedAssetPath = null;
        }

        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && _inlineRenamePath == null && !ImGui.IsAnyItemActive())
        {
            bool hasSelection = _selectedPath != null;
            if (ImGui.GetIO().KeyCtrl)
            {
                if (hasSelection && ImGui.IsKeyPressed(ImGuiKey.C)) CopySelected(cut: false);
                else if (hasSelection && ImGui.IsKeyPressed(ImGuiKey.X)) CopySelected(cut: true);
                else if (hasSelection && ImGui.IsKeyPressed(ImGuiKey.D)) DuplicateAsset(_selectedPath!);
                else if (ImGui.IsKeyPressed(ImGuiKey.V)) PasteClipboardInto(_currentDirectory);
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
                    _isDeleting = true;
                    _deleteTarget = _selectedPath!;
                }
            }
        }
    }

    private void DrawTile(AssetEntry entry, float cellW, float cellH, float pad)
    {
        var palette = EditorThemeManager.Current.Palette;
        var drawList = ImGui.GetWindowDrawList();

        ImGui.PushID(entry.FullPath);
        Vector2 p0 = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("tile", new Vector2(cellW, cellH));

        bool hovered = ImGui.IsItemHovered();
        bool selected = _selectedPath == entry.FullPath;

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            _selectedPath = entry.FullPath;
        }
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (entry.IsDirectory)
            {
                _pendingNavigate = entry.FullPath;
            }
            else if (entry.Kind == AssetKind.Material || entry.Kind == AssetKind.Prefab)
            {
                // Open the material/prefab in the Inspector for editing (mirrors how scenes open on double-click).
                _context.Selection = null;
                _context.SelectedAssetPath = entry.FullPath;
            }
            else if (entry.Kind == AssetKind.Scene)
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
        if (ImGui.BeginDragDropSource())
        {
            _dragPath = entry.FullPath;
            (string payloadType, string payloadData) = DragPayloadFor(entry);
            SetDragPayload(payloadType, payloadData);
            ImGui.Text(entry.Name);
            ImGui.EndDragDropSource();
        }

        // Drop onto a folder tile to move the dragged asset (or folder) into it.
        if (entry.IsDirectory && ImGui.BeginDragDropTarget())
        {
            if (TryAcceptAssetMove())
            {
                MoveEntryInto(_dragPath, entry.FullPath);
            }
            ImGui.EndDragDropTarget();
        }

        DrawItemContextMenu(entry);

        // Backgrounds: subtle card, brighter on hover, accent when selected.
        uint bg = selected
            ? ImGui.GetColorU32(WithAlpha(palette.Accent, 0.35f))
            : hovered
                ? ImGui.GetColorU32(palette.FrameBgHovered)
                : ImGui.GetColorU32(WithAlpha(palette.FrameBg, 0.5f));
        drawList.AddRectFilled(p0, p0 + new Vector2(cellW, cellH), bg, 5.0f);

        Vector2 iconMin = p0 + new Vector2(pad, pad);
        DrawIcon(drawList, iconMin, _iconSize, entry, palette);

        bool isInlineRenaming = _inlineRenamePath == entry.FullPath;

        if (isInlineRenaming)
        {
            // Draw an InputText below the icon instead of the static label.
            Vector2 inputPos = new Vector2(p0.X + 4, p0.Y + pad + _iconSize + 1);
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
            string label = entry.Name;
            if (ImGui.CalcTextSize(label).X > cellW - 6)
            {
                float eWidth = ImGui.CalcTextSize("...").X;
                for (int i = label.Length - 1; i > 0; i--)
                {
                    if (ImGui.CalcTextSize(label.Substring(0, i)).X + eWidth <= cellW - 6)
                    {
                        label = label.Substring(0, i) + "...";
                        break;
                    }
                }
            }
            Vector2 ts = ImGui.CalcTextSize(label);
            Vector2 labelPos = new Vector2(p0.X + (cellW - ts.X) * 0.5f, p0.Y + pad + _iconSize + 3);
            drawList.AddText(labelPos, ImGui.GetColorU32(palette.Text), label);

            if (hovered)
            {
                ImGui.SetTooltip(entry.Name);
            }
        }

        ImGui.PopID();
    }

    // Per-kind accent colors for the asset icons.
    private static readonly Vector4 ScriptColor = new(0.36f, 0.66f, 0.98f, 1.0f);
    private static readonly Vector4 SceneColor = new(0.66f, 0.40f, 0.98f, 1.0f);
    private static readonly Vector4 ImageColor = new(0.30f, 0.80f, 0.55f, 1.0f);
    private static readonly Vector4 ModelColor = new(0.98f, 0.62f, 0.26f, 1.0f);
    private static readonly Vector4 MaterialColor = new(0.42f, 0.72f, 1.00f, 1.0f);
    private static readonly Vector4 PrefabColor = new(0.40f, 0.82f, 0.92f, 1.0f);
    private static readonly Vector4 AudioColor = new(0.95f, 0.55f, 0.75f, 1.0f);

    private static AudioClip? _previewClip;
    private static Voice _previewVoice;

    private static void PlayAudioPreview(string sourcePath)
    {
        try
        {
            StopAudioPreview();
            _previewClip = AudioClip.LoadRef(sourcePath); // a source path decodes directly, no cooking needed
            _previewVoice = AudioManager.Play(_previewClip, spatial: false);
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to preview audio '{0}': {1}", sourcePath, ex.Message);
        }
    }

    private static void StopAudioPreview()
    {
        AudioManager.Stop(_previewVoice);
        _previewVoice = default;
        _previewClip?.Dispose();
        _previewClip = null;
    }

    private void DrawIcon(ImDrawListPtr drawList, Vector2 iconMin, float size, AssetEntry entry, EditorPalette palette)
    {
        Vector2 iconMax = iconMin + new Vector2(size, size);

        // Dynamic previews take priority and keep their existing look (thumbnail / rendered material).
        if (entry.Kind == AssetKind.Image && TryGetThumbnail(entry.FullPath, out var tex))
        {
            drawList.AddRectFilled(iconMin, iconMax, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.35f)), 4.0f);
            float scale = Math.Min(size / tex.Width, size / tex.Height);
            float w = tex.Width * scale;
            float h = tex.Height * scale;
            Vector2 imgMin = iconMin + new Vector2((size - w) * 0.5f, (size - h) * 0.5f);
            drawList.AddImage((IntPtr)tex.Handle, imgMin, imgMin + new Vector2(w, h), new Vector2(0, 1), new Vector2(1, 0));
            return;
        }

        if (entry.Kind == AssetKind.Material && TryGetMaterialPreview(entry.FullPath, out var matFb))
        {
            drawList.AddRectFilled(iconMin, iconMax, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.35f)), 4.0f);
            drawList.AddImage((IntPtr)matFb.ColorAttachment, iconMin, iconMax, new Vector2(0, 1), new Vector2(1, 0));
            return;
        }

        // Everything else is a centered icon-font glyph tinted per kind — the same font the Hierarchy and
        // viewport use, drawn from the large icon atlas so it stays crisp at tile sizes (48–128px).
        (string glyph, Vector4 color) = GlyphFor(entry.Kind, palette);
        DrawGlyph(drawList, iconMin, size, glyph, color);
    }

    // Picks the Font Awesome glyph and tint for a non-preview asset kind.
    private static (string Glyph, Vector4 Color) GlyphFor(AssetKind kind, EditorPalette palette) => kind switch
    {
        AssetKind.Folder => (EditorIcons.FolderOpen, palette.TextDisabled),
        AssetKind.Script => (EditorIcons.Code, ScriptColor),
        AssetKind.Scene => (EditorIcons.Cubes, SceneColor),
        AssetKind.Image => (EditorIcons.Image, ImageColor),
        AssetKind.Model => (EditorIcons.Cube, ModelColor),
        AssetKind.Material => (EditorIcons.Palette, MaterialColor),
        AssetKind.Prefab => (EditorIcons.Sitemap, PrefabColor),
        AssetKind.Audio => (EditorIcons.Music, AudioColor),
        _ => (EditorIcons.File, palette.TextDisabled),
    };

    // Draws a single icon-font glyph centered in the icon box, scaled to ~62% of it for breathing room.
    private static void DrawGlyph(ImDrawListPtr dl, Vector2 iconMin, float size, string glyph, Vector4 color)
    {
        ImFontPtr font = EditorFonts.Icons;
        float glyphPx = size * 0.62f;
        Vector2 ts = font.CalcTextSizeA(glyphPx, float.MaxValue, 0.0f, glyph);
        Vector2 center = iconMin + new Vector2(size * 0.5f, size * 0.5f);
        dl.AddText(font, glyphPx, center - ts * 0.5f, ImGui.GetColorU32(color), glyph);
    }

    private void DrawItemContextMenu(AssetEntry entry)
    {
        if (!ImGui.BeginPopupContextItem("itemctx"))
        {
            return;
        }

        _selectedPath = entry.FullPath;

        if (entry.Kind == AssetKind.Material && ImGui.MenuItem("Edit Material"))
        {
            _context.Selection = null;
            _context.SelectedAssetPath = entry.FullPath;
        }

        if (entry.Kind == AssetKind.Model && ImGui.MenuItem("Extract Materials (Embedded)"))
        {
            Spot.Assets.AssimpModelImporter.ExtractMaterials(entry.FullPath);
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
        else
        {
            if (ImGui.MenuItem("Open Externally")) OpenExternally(entry.FullPath);
        }
        if (ImGui.MenuItem("Show in Explorer"))
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
        if (ImGui.MenuItem("Delete"))
        {
            _isDeleting = true;
            _deleteTarget = entry.FullPath;
        }

        ImGui.EndPopup();
    }

    private void DrawEmptySpaceContextMenu()
    {
        if (!ImGui.BeginPopupContextWindow("AssetBrowserContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            return;
        }

        if (ImGui.MenuItem("New Folder"))
        {
            string path = UniqueAssetPath(_currentDirectory, "New Folder", "");
            try { Directory.CreateDirectory(path); } catch { }
            StartInlineRename(path, Path.GetFileName(path), isNew: true);
        }
        if (ImGui.MenuItem("New Script"))
        {
            string path = UniqueAssetPath(_currentDirectory, "NewScript", ".cs");
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
        ImGui.Separator();
        if (ImGui.MenuItem("Paste", "Ctrl+V", false, s_clipboardPath != null))
        {
            PasteClipboardInto(_currentDirectory);
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Open in Explorer"))
        {
            OpenExternally(_currentDirectory);
        }
        ImGui.EndPopup();
    }

    private void StartInlineRename(string fullPath, string bufferInitial, bool isNew = false)
    {
        _selectedPath = fullPath;
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
            ImGui.TextUnformatted($"Delete '{Path.GetFileName(_deleteTarget)}'?");
            if (Directory.Exists(_deleteTarget))
            {
                ImGui.TextColored(EditorThemeManager.Current.Palette.LogError, "This folder and all its contents will be removed.");
            }
            ImGui.Spacing();
            if (ImGui.Button("Delete", new Vector2(120, 0)))
            {
                DeleteEntry(_deleteTarget);
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

    private List<AssetEntry> GatherEntries()
    {
        var result = new List<AssetEntry>();
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
                result.Add(new AssetEntry(dir.FullName, dir.Name, true, AssetKind.Folder));
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
            texture = new Texture2D(path);
            _thumbnails[path] = texture;
            return true;
        }
        catch
        {
            _thumbFailed.Add(path);
            return false;
        }
    }

    private bool TryGetMaterialPreview(string path, out Spot.Rendering.Framebuffer fb)
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
            fb = new Spot.Rendering.Framebuffer(128, 128);
            var material = Spot.Assets.Material.Load(path);
            Spot.Editor.UI.MaterialPreviewHelper.RenderToFramebuffer(material, fb);
            _materialPreviews[path] = fb;
            return true;
        }
        catch
        {
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
        string template = $@"using System;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Game;

public class {className} : EntityBehaviour
{{
    public override void OnCreate()
    {{

    }}

    public override void OnUpdate(float deltaTime)
    {{

    }}
}}
";
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
        
        var newScene = new Spot.Scenes.Scene();
        new Spot.Scenes.SceneSerializer(newScene).Serialize(filepath);
    }

    private void CreateMaterial(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!name.EndsWith(".sptmat")) name += ".sptmat";
        EnsureDirectory(_currentDirectory);
        string filepath = Path.Combine(_currentDirectory, name);
        if (File.Exists(filepath)) return;

        new Spot.Assets.Material().Save(filepath);
    }

    // Accepts an entity dragged from the hierarchy onto the asset grid, writing it out as a .sptprefab in the
    // current folder and marking the source entity as an instance of the new prefab.
    private void AcceptEntityDropToCreatePrefab()
    {
        if (!ImGui.BeginDragDropTarget())
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
            string? reference = Spot.Assets.AssetDatabase.ToGuidRef(path);
            entity.AddComponent(new PrefabComponent { PrefabRef = reference });

            _selectedPath = path;
            ClearThumbnails();
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to create prefab: {0}", ex.Message);
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
            RenameEntry(entry.FullPath, newName);
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
            if (_context.SelectedAssetPath == fullPath) _context.SelectedAssetPath = dest;
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to rename asset: {0}", ex.Message);
        }
        ClearThumbnails();
    }

    // Payload types a folder tile accepts as a move. Mirrors the types produced by DragPayloadFor.
    private static readonly string[] MovablePayloads =
    {
        "FOLDER_FILE", "IMAGE_FILE", "MODEL_FILE", "MATERIAL_FILE",
        "SCENE_FILE", "PREFAB_FILE", "AUDIO_FILE", "SCRIPT_FILE",
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
                Spot.Core.Log.Error("Cannot move '{0}': an item with that name already exists in the target folder.", name);
                return;
            }

            if (!MovePath(sourcePath, dest)) return;

            if (_selectedPath == sourcePath) _selectedPath = dest;
            if (_context.SelectedAssetPath == sourcePath) _context.SelectedAssetPath = dest;
            ClearThumbnails();
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to move asset: {0}", ex.Message);
        }
        finally
        {
            _dragPath = null;
        }
    }

    // Compact Copy / Cut / Duplicate / Paste button strip beneath the toolbar. Mirrors the shortcuts and
    // per-item context menu so the actions are discoverable without right-clicking.
    private void DrawActionBar()
    {
        bool hasSelection = _selectedPath != null && (File.Exists(_selectedPath) || Directory.Exists(_selectedPath));
        bool canPaste = s_clipboardPath != null;

        ImGui.BeginDisabled(!hasSelection);
        if (ImGui.SmallButton("Copy")) CopySelected(cut: false);
        ImGui.SameLine();
        if (ImGui.SmallButton("Cut")) CopySelected(cut: true);
        ImGui.SameLine();
        if (ImGui.SmallButton("Duplicate")) DuplicateAsset(_selectedPath!);
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!canPaste);
        if (ImGui.SmallButton("Paste")) PasteClipboardInto(_currentDirectory);
        ImGui.EndDisabled();
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
            _selectedPath = dest;
            ClearThumbnails();
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to duplicate asset: {0}", ex.Message);
        }
    }

    // Pastes the clipboard asset into destDir. A cut moves (carrying the .meta guid) and is then consumed; a
    // copy duplicates as a fresh asset. Names that collide in the target get a unique suffix.
    private void PasteClipboardInto(string destDir)
    {
        if (string.IsNullOrEmpty(s_clipboardPath)) return;
        string src = s_clipboardPath;

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
                    _selectedPath = dest;
                    s_clipboardPath = null; // a cut is consumed by its paste
                }
            }
            else
            {
                CopyPath(src, dest);
                _selectedPath = dest;
            }
            ClearThumbnails();
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to paste asset: {0}", ex.Message);
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
            string meta = Spot.Assets.AssetMeta.MetaPathFor(src);
            if (File.Exists(meta)) File.Move(meta, Spot.Assets.AssetMeta.MetaPathFor(dest));
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
            if (_context.SelectedAssetPath == fullPath) _context.SelectedAssetPath = null;
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to delete asset: {0}", ex.Message);
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
        _selectedPath = null;
        ClearThumbnails();
    }

    private void ClearThumbnails()
    {
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
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
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
