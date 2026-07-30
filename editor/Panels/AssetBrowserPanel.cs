using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.Editor.UI;
using Spot.Rendering;

namespace Spot.Editor.Panels;

public class AssetBrowserPanel
{
    private enum AssetKind { Folder, Script, Scene, Image, Other }

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
    private const int MaxThumbnails = 128;

    private readonly EditorContext _context;
    private string _currentDirectory;
    private string _baseDirectory;

    private string _searchQuery = "";
    private float _iconSize = 72.0f;
    private string? _selectedPath;
    private string? _pendingNavigate;

    // Deferred creation / editing state (driven by context menus, resolved as modals).
    private bool _isCreatingScript;
    private string _newScriptName = "";
    private bool _isCreatingFolder;
    private string _newFolderName = "";
    private bool _isRenaming;
    private string _renameTarget = "";
    private string _renameBuffer = "";
    private bool _isDeleting;
    private string _deleteTarget = "";
    private bool _isCreatingScene;
    private string _newSceneName = "";

    // Thumbnail cache for the current directory (disposed when the directory changes).
    private readonly Dictionary<string, Texture2D> _thumbnails = new();
    private readonly HashSet<string> _thumbFailed = new();

    public Action<string>? OnAssetOpened;

    public AssetBrowserPanel(EditorContext context)
    {
        _context = context;
        _baseDirectory = Spot.Core.Project.Active?.GetAssetDirectory() ?? Environment.CurrentDirectory;
        EnsureDirectory(_baseDirectory);
        _currentDirectory = _baseDirectory;
    }

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
        ImGui.Separator();

        ImGui.BeginChild("AssetGrid", new Vector2(0, 0), ImGuiChildFlags.None);
        DrawGrid();
        DrawEmptySpaceContextMenu();
        ImGui.EndChild();

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

        if (ImGui.Button(BaseName(_baseDirectory)))
        {
            _pendingNavigate = _baseDirectory;
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
            }
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);

        // Right-aligned search box and thumbnail-size slider.
        const float sliderWidth = 90.0f;
        const float searchWidth = 200.0f;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float rightGroup = searchWidth + spacing + sliderWidth;
        float offset = ImGui.GetContentRegionAvail().X - rightGroup;
        if (offset > 0)
        {
            ImGui.SameLine(0, offset);
        }
        else
        {
            ImGui.SameLine();
        }

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

        float pad = 8.0f;
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
            else
            {
                if (OnAssetOpened != null) OnAssetOpened.Invoke(entry.FullPath);
                else OpenExternally(entry.FullPath);
            }
        }

        // Drag files as payloads (consumed by the Inspector).
        if (!entry.IsDirectory && ImGui.BeginDragDropSource())
        {
            if (entry.Kind == AssetKind.Image)
            {
                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(entry.FullPath + "\0");
                unsafe
                {
                    fixed (byte* p = payloadBytes)
                    {
                        ImGui.SetDragDropPayload("IMAGE_FILE", (IntPtr)p, (uint)payloadBytes.Length);
                    }
                }
            }
            else
            {
                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(entry.Name + "\0");
                unsafe
                {
                    fixed (byte* p = payloadBytes)
                    {
                        ImGui.SetDragDropPayload("SCRIPT_FILE", (IntPtr)p, (uint)payloadBytes.Length);
                    }
                }
            }
            ImGui.Text(entry.Name);
            ImGui.EndDragDropSource();
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

        ImGui.PopID();
    }

    private void DrawIcon(ImDrawListPtr drawList, Vector2 iconMin, float size, AssetEntry entry, EditorPalette palette)
    {
        Vector2 iconMax = iconMin + new Vector2(size, size);

        if (entry.Kind == AssetKind.Folder)
        {
            uint folder = ImGui.GetColorU32(palette.Accent);
            var tabMin = iconMin + new Vector2(size * 0.12f, size * 0.24f);
            var tabMax = iconMin + new Vector2(size * 0.48f, size * 0.40f);
            var bodyMin = iconMin + new Vector2(size * 0.12f, size * 0.34f);
            var bodyMax = iconMin + new Vector2(size * 0.88f, size * 0.78f);
            drawList.AddRectFilled(tabMin, tabMax, folder, 3.0f);
            drawList.AddRectFilled(bodyMin, bodyMax, folder, 4.0f);
            return;
        }

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

        // File "page" with a type badge.
        var pageMin = iconMin + new Vector2(size * 0.24f, size * 0.10f);
        var pageMax = iconMin + new Vector2(size * 0.76f, size * 0.90f);
        drawList.AddRectFilled(pageMin, pageMax, ImGui.GetColorU32(WithAlpha(palette.Text, 0.10f)), 4.0f);
        drawList.AddRect(pageMin, pageMax, ImGui.GetColorU32(WithAlpha(palette.Text, 0.25f)), 4.0f);

        (string badge, Vector4 color) = entry.Kind switch
        {
            AssetKind.Script => ("C#", palette.Accent),
            AssetKind.Scene => ("SCENE", new Vector4(0.66f, 0.40f, 0.98f, 1.0f)),
            AssetKind.Image => ("IMG", new Vector4(0.30f, 0.80f, 0.55f, 1.0f)),
            _ => (BadgeFor(entry.Name), palette.TextDisabled),
        };

        Vector2 badgeSize = ImGui.CalcTextSize(badge);
        Vector2 center = (pageMin + pageMax) * 0.5f;
        drawList.AddText(center - badgeSize * 0.5f, ImGui.GetColorU32(color), badge);
    }

    private void DrawItemContextMenu(AssetEntry entry)
    {
        if (!ImGui.BeginPopupContextItem("itemctx"))
        {
            return;
        }

        _selectedPath = entry.FullPath;

        if (ImGui.MenuItem(entry.IsDirectory ? "Open" : "Open Externally"))
        {
            if (entry.IsDirectory) _pendingNavigate = entry.FullPath;
            else if (OnAssetOpened != null) OnAssetOpened.Invoke(entry.FullPath);
            else OpenExternally(entry.FullPath);
        }
        if (ImGui.MenuItem("Show in Explorer"))
        {
            RevealInExplorer(entry.FullPath);
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Rename"))
        {
            _isRenaming = true;
            _renameTarget = entry.FullPath;
            _renameBuffer = entry.Name;
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
            _isCreatingFolder = true;
            _newFolderName = "New Folder";
        }
        if (ImGui.MenuItem("New Script"))
        {
            _isCreatingScript = true;
            _newScriptName = "NewScript.cs";
        }
        if (ImGui.MenuItem("New Scene"))
        {
            _isCreatingScene = true;
            _newSceneName = "NewScene.sptscene";
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Open in Explorer"))
        {
            OpenExternally(_currentDirectory);
        }
        ImGui.EndPopup();
    }

    private void HandleModals()
    {
        if (_isCreatingScript) ImGui.OpenPopup("Create New Script");
        if (_isCreatingFolder) ImGui.OpenPopup("Create New Folder");
        if (_isCreatingScene) ImGui.OpenPopup("Create New Scene");
        if (_isRenaming) ImGui.OpenPopup("Rename");
        if (_isDeleting) ImGui.OpenPopup("Delete Asset");

        DrawTextEntryModal("Create New Script", "Name", ref _isCreatingScript, ref _newScriptName, CreateScript);
        DrawTextEntryModal("Create New Folder", "Name", ref _isCreatingFolder, ref _newFolderName, CreateFolder);
        DrawTextEntryModal("Create New Scene", "Name", ref _isCreatingScene, ref _newSceneName, CreateScene);
        DrawTextEntryModal("Rename", "New name", ref _isRenaming, ref _renameBuffer, name => RenameEntry(_renameTarget, name));

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

    private static void DrawTextEntryModal(string id, string fieldLabel, ref bool open, ref string buffer, Action<string> onConfirm)
    {
        bool windowOpen = true;
        if (ImGui.BeginPopupModal(id, ref windowOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.SetNextItemWidth(260);
            bool submitted = ImGui.InputText("##" + fieldLabel, ref buffer, 256, ImGuiInputTextFlags.EnterReturnsTrue);
            if (ImGui.Button("OK", new Vector2(120, 0)) || submitted)
            {
                onConfirm(buffer);
                open = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                open = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        else if (!windowOpen)
        {
            open = false;
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
        return AssetKind.Other;
    }

    private static string BadgeFor(string name)
    {
        string ext = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrEmpty(ext) ? "FILE" : ext;
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
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to rename asset: {0}", ex.Message);
        }
        ClearThumbnails();
    }

    private void DeleteEntry(string fullPath)
    {
        try
        {
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
            else if (File.Exists(fullPath)) File.Delete(fullPath);
            if (_selectedPath == fullPath) _selectedPath = null;
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
