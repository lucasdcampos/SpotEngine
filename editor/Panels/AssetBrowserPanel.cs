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
    private enum AssetKind { Folder, Script, Scene, Image, Model, Material, Other }

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
    private bool _isCreatingMaterial;
    private string _newMaterialName = "";

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
            _context.SelectedAssetPath = null;
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
            else if (entry.Kind == AssetKind.Material)
            {
                // Open the material in the Inspector for editing (mirrors how scenes open on double-click).
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
            else if (entry.Kind == AssetKind.Model)
            {
                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(entry.FullPath + "\0");
                unsafe
                {
                    fixed (byte* p = payloadBytes)
                    {
                        ImGui.SetDragDropPayload("MODEL_FILE", (IntPtr)p, (uint)payloadBytes.Length);
                    }
                }
            }
            else if (entry.Kind == AssetKind.Material)
            {
                var payloadBytes = System.Text.Encoding.UTF8.GetBytes(entry.FullPath + "\0");
                unsafe
                {
                    fixed (byte* p = payloadBytes)
                    {
                        ImGui.SetDragDropPayload("MATERIAL_FILE", (IntPtr)p, (uint)payloadBytes.Length);
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

    // Per-kind accent colors for the asset icons.
    private static readonly Vector4 ScriptColor = new(0.36f, 0.66f, 0.98f, 1.0f);
    private static readonly Vector4 SceneColor = new(0.66f, 0.40f, 0.98f, 1.0f);
    private static readonly Vector4 ImageColor = new(0.30f, 0.80f, 0.55f, 1.0f);
    private static readonly Vector4 ModelColor = new(0.98f, 0.62f, 0.26f, 1.0f);
    private static readonly Vector4 MaterialColor = new(0.42f, 0.72f, 1.00f, 1.0f);

    private void DrawIcon(ImDrawListPtr drawList, Vector2 iconMin, float size, AssetEntry entry, EditorPalette palette)
    {
        Vector2 iconMax = iconMin + new Vector2(size, size);
        Vector2 center = iconMin + new Vector2(size * 0.5f, size * 0.5f);

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

        switch (entry.Kind)
        {
            case AssetKind.Script: DrawScriptIcon(drawList, center, size); break;
            case AssetKind.Scene: DrawSceneIcon(drawList, center, size); break;
            case AssetKind.Image: DrawImageIcon(drawList, center, size); break;
            case AssetKind.Model: DrawModelIcon(drawList, center, size); break;
            case AssetKind.Material: DrawMaterialIcon(drawList, center, size); break;
            default: DrawGenericFileIcon(drawList, center, size, palette); break;
        }
    }

    // ----- Asset type icons (drawn as vector primitives, sized relative to the icon box) -----------

    // A document page with a folded corner. Returns the page rect so callers can add glyphs inside.
    private static void DrawPage(ImDrawListPtr dl, Vector2 c, float size, Vector4 accent,
                                 out Vector2 pageMin, out Vector2 pageMax)
    {
        float w = size * 0.50f;
        float h = size * 0.66f;
        pageMin = c - new Vector2(w * 0.5f, h * 0.5f);
        pageMax = c + new Vector2(w * 0.5f, h * 0.5f);
        float fold = size * 0.17f;
        float rounding = size * 0.05f;
        float thick = Thickness(size);

        uint fill = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.10f));
        uint border = ImGui.GetColorU32(WithAlpha(accent, 0.95f));

        // Body (leave the top-right corner squared so the fold reads cleanly).
        dl.AddRectFilled(pageMin, pageMax, fill, rounding, ImDrawFlags.RoundCornersBottom | ImDrawFlags.RoundCornersTopLeft);
        dl.AddRect(pageMin, pageMax, border, rounding, ImDrawFlags.RoundCornersBottom | ImDrawFlags.RoundCornersTopLeft, thick);

        // Folded corner triangle.
        Vector2 fA = new(pageMax.X - fold, pageMin.Y);
        Vector2 fB = new(pageMax.X, pageMin.Y + fold);
        Vector2 fC = new(pageMax.X - fold, pageMin.Y + fold);
        dl.AddTriangleFilled(fA, fB, fC, ImGui.GetColorU32(WithAlpha(accent, 0.45f)));
        dl.AddLine(fA, fC, border, thick);
        dl.AddLine(fC, fB, border, thick);
    }

    private static void DrawScriptIcon(ImDrawListPtr dl, Vector2 c, float size)
    {
        DrawPage(dl, c, size, ScriptColor, out _, out _);
        uint col = ImGui.GetColorU32(ScriptColor);
        float th = Thickness(size);

        // A "</>" code glyph centered on the page.
        dl.AddLine(new Vector2(c.X - size * 0.06f, c.Y - size * 0.10f), new Vector2(c.X - size * 0.16f, c.Y), col, th);
        dl.AddLine(new Vector2(c.X - size * 0.16f, c.Y), new Vector2(c.X - size * 0.06f, c.Y + size * 0.10f), col, th);
        dl.AddLine(new Vector2(c.X + size * 0.06f, c.Y - size * 0.10f), new Vector2(c.X + size * 0.16f, c.Y), col, th);
        dl.AddLine(new Vector2(c.X + size * 0.16f, c.Y), new Vector2(c.X + size * 0.06f, c.Y + size * 0.10f), col, th);
        dl.AddLine(new Vector2(c.X - size * 0.02f, c.Y + size * 0.12f), new Vector2(c.X + size * 0.02f, c.Y - size * 0.12f), col, th);
    }

    private static void DrawGenericFileIcon(ImDrawListPtr dl, Vector2 c, float size, EditorPalette palette)
    {
        DrawPage(dl, c, size, palette.TextDisabled, out Vector2 pageMin, out Vector2 pageMax);
        uint col = ImGui.GetColorU32(WithAlpha(palette.Text, 0.5f));
        float th = Thickness(size);

        // A few "text lines" to suggest a generic document.
        float w = pageMax.X - pageMin.X;
        float x0 = pageMin.X + w * 0.20f;
        float x1 = pageMax.X - w * 0.20f;
        for (int i = 0; i < 3; i++)
        {
            float y = c.Y - size * 0.06f + i * size * 0.10f;
            dl.AddLine(new Vector2(x0, y), new Vector2(x1, y), col, th);
        }
    }

    private static void DrawSceneIcon(ImDrawListPtr dl, Vector2 c, float size)
    {
        // A stack of layers (three isometric diamonds).
        float hw = size * 0.28f;
        float hh = size * 0.13f;
        float th = Thickness(size);
        uint line = ImGui.GetColorU32(SceneColor);
        for (int k = 2; k >= 0; k--)
        {
            Vector2 dc = new(c.X, c.Y + (k - 1) * size * 0.14f);
            Vector2 top = new(dc.X, dc.Y - hh);
            Vector2 right = new(dc.X + hw, dc.Y);
            Vector2 bottom = new(dc.X, dc.Y + hh);
            Vector2 left = new(dc.X - hw, dc.Y);
            uint fill = ImGui.GetColorU32(WithAlpha(SceneColor, k == 0 ? 0.6f : 0.22f));
            dl.AddTriangleFilled(top, right, bottom, fill);
            dl.AddTriangleFilled(top, bottom, left, fill);
            dl.AddLine(top, right, line, th);
            dl.AddLine(right, bottom, line, th);
            dl.AddLine(bottom, left, line, th);
            dl.AddLine(left, top, line, th);
        }
    }

    private static void DrawModelIcon(ImDrawListPtr dl, Vector2 c, float size)
    {
        // A cube seen corner-on: hexagon silhouette with three inner edges.
        float r = size * 0.30f;
        float th = Thickness(size);
        uint line = ImGui.GetColorU32(ModelColor);
        uint fill = ImGui.GetColorU32(WithAlpha(ModelColor, 0.20f));

        Span<Vector2> hex = stackalloc Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float a = (MathF.PI / 180.0f) * (30.0f + 60.0f * i);
            hex[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
        }
        for (int i = 0; i < 6; i++)
            dl.AddTriangleFilled(c, hex[i], hex[(i + 1) % 6], fill);
        for (int i = 0; i < 6; i++)
            dl.AddLine(hex[i], hex[(i + 1) % 6], line, th);
        dl.AddLine(c, hex[1], line, th);
        dl.AddLine(c, hex[3], line, th);
        dl.AddLine(c, hex[5], line, th);
    }

    private static void DrawMaterialIcon(ImDrawListPtr dl, Vector2 c, float size)
    {
        // A shaded sphere, the conventional material preview.
        float r = size * 0.30f;
        dl.AddCircleFilled(c, r, ImGui.GetColorU32(WithAlpha(MaterialColor, 0.9f)), 32);
        dl.AddCircle(c, r, ImGui.GetColorU32(Darken(MaterialColor, 0.35f)), 32, Thickness(size));
        dl.AddCircleFilled(c - new Vector2(r * 0.32f, r * 0.32f), r * 0.30f,
            ImGui.GetColorU32(new Vector4(1, 1, 1, 0.45f)), 20);
    }

    private static void DrawImageIcon(ImDrawListPtr dl, Vector2 c, float size)
    {
        // A framed picture with a sun and mountains (used when a thumbnail is unavailable).
        float w = size * 0.60f;
        float h = size * 0.48f;
        Vector2 mn = c - new Vector2(w * 0.5f, h * 0.5f);
        Vector2 mx = c + new Vector2(w * 0.5f, h * 0.5f);
        float th = Thickness(size);
        uint col = ImGui.GetColorU32(ImageColor);

        dl.AddRectFilled(mn, mx, ImGui.GetColorU32(WithAlpha(ImageColor, 0.16f)), size * 0.04f);
        dl.AddRect(mn, mx, col, size * 0.04f, ImDrawFlags.None, th);
        dl.AddCircleFilled(new Vector2(mn.X + w * 0.26f, mn.Y + h * 0.28f), size * 0.05f, col, 12);
        uint hill = ImGui.GetColorU32(WithAlpha(ImageColor, 0.7f));
        dl.AddTriangleFilled(new Vector2(mn.X + w * 0.08f, mx.Y), new Vector2(mn.X + w * 0.42f, mn.Y + h * 0.45f), new Vector2(mn.X + w * 0.72f, mx.Y), hill);
        dl.AddTriangleFilled(new Vector2(mn.X + w * 0.5f, mx.Y), new Vector2(mn.X + w * 0.74f, mn.Y + h * 0.55f), new Vector2(mx.X - w * 0.04f, mx.Y), hill);
    }

    private static float Thickness(float size) => MathF.Max(1.3f, size * 0.022f);

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
        if (ImGui.MenuItem("New Material"))
        {
            _isCreatingMaterial = true;
            _newMaterialName = "NewMaterial.sptmat";
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
        if (_isCreatingMaterial) ImGui.OpenPopup("Create New Material");
        if (_isRenaming) ImGui.OpenPopup("Rename");
        if (_isDeleting) ImGui.OpenPopup("Delete Asset");

        DrawTextEntryModal("Create New Script", "Name", ref _isCreatingScript, ref _newScriptName, CreateScript);
        DrawTextEntryModal("Create New Folder", "Name", ref _isCreatingFolder, ref _newFolderName, CreateFolder);
        DrawTextEntryModal("Create New Scene", "Name", ref _isCreatingScene, ref _newSceneName, CreateScene);
        DrawTextEntryModal("Create New Material", "Name", ref _isCreatingMaterial, ref _newMaterialName, CreateMaterial);
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
        if (ModelExtensions.Contains(ext)) return AssetKind.Model;
        if (ext == ".sptmat") return AssetKind.Material;
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

    private static Vector4 Darken(Vector4 c, float amount) => new(
        Math.Clamp(c.X - amount, 0.0f, 1.0f),
        Math.Clamp(c.Y - amount, 0.0f, 1.0f),
        Math.Clamp(c.Z - amount, 0.0f, 1.0f),
        c.W);

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
