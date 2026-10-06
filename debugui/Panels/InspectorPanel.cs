using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.Engine.Assets;
using Spot.Engine;
using Spot.Engine.Scenes;
using Spot.Engine.Graphics;
using Spot.DebugUI.UI;

namespace Spot.DebugUI.Panels;

public class InspectorPanel : IDisposable
{
    private readonly ISelectionContext _context;
    private Spot.Engine.Graphics.Framebuffer? _materialPreviewFb;
    // The material path and property fingerprint the preview framebuffer was last rendered for, so the
    // offscreen render only re-runs when something the preview shows actually changed.
    private string? _materialPreviewPath;
    private int _materialPreviewSig;
    // The material path we last logged a preview-render failure for, so a broken material logs once instead
    // of every frame the inspector is open.
    private string? _materialPreviewErrorPath;

    // The preview of the selected built-in mesh (rendered once per selection).
    private Spot.Engine.Graphics.Framebuffer? _builtinPreviewFb;
    private string? _builtinPreviewReference;

    // Prefab editing state: the inspected prefab is loaded into an isolated scene so its components can be
    // edited with the same reflection-based UI as a live entity, then re-serialized back to disk on change.
    private Scene? _prefabScene;
    private Entity? _prefabRoot;
    private string? _prefabPath;
    private string _prefabLastJson = "";
    private string _componentSearchFilter = "";

    // The Add Component popup's state: the game's component types (gathered when the popup opens, since that
    // scans the loaded assemblies) and the "New Component" naming step.
    private List<Type> _userComponentTypes = new();
    private bool _namingNewComponent;
    private bool _focusNewComponentName;
    private string _newComponentName = "";

    public InspectorPanel(ISelectionContext context)
    {
        _context = context;
        ComponentScripts.LoadedTypesForgotten += ForgetLoadedTypes;
    }

    // The game's scripts are being reloaded: let go of their types and of the inspected prefab's components
    // (which are instances of them), so the old assembly can unload. The prefab reloads on its next draw.
    private void ForgetLoadedTypes()
    {
        _userComponentTypes = new List<Type>();
        _prefabScene = null;
        _prefabRoot = null;
        _prefabPath = null;
    }

    // Releases the material-preview framebuffer's GL resources. Called when the editor shuts down.
    public void Dispose()
    {
        ComponentScripts.LoadedTypesForgotten -= ForgetLoadedTypes;
        _materialPreviewFb?.Dispose();
        _materialPreviewFb = null;
        _builtinPreviewFb?.Dispose();
        _builtinPreviewFb = null;
        GC.SuppressFinalize(this);
    }

    public void OnImGuiRender(ref bool open)
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse;
        ImGui.Begin("Properties", ref open, flags);
        EditorGui.MarkFocusedTab();

        if (_context.SelectedAssetPath != null && BuiltinAssets.TryGet(_context.SelectedAssetPath, out BuiltinAsset builtin))
        {
            DrawBuiltinAsset(builtin);
        }
        else if (_context.SelectedAssetPath != null && _context.SelectedAssetPath.EndsWith(".sptmat", StringComparison.OrdinalIgnoreCase))
        {
            DrawMaterialEditor(_context.SelectedAssetPath);
        }
        else if (_context.SelectedAssetPath != null && _context.SelectedAssetPath.EndsWith(".sptprefab", StringComparison.OrdinalIgnoreCase))
        {
            DrawPrefabEditor(_context.SelectedAssetPath);
        }
        else if (_context.SelectedWidget != null)
        {
            WidgetInspector.Draw(_context.SelectedWidget);
        }
        else if (_context.Selection != null)
        {
            Entity entity = _context.Selection.Value;
            DrawComponents(entity);

            ImGui.Spacing();
            DrawAddComponentButton(entity);
        }

        ImGui.End();
    }

    private void DrawComponents(Entity entity)
    {
        DrawTagRow(entity);
        ImGui.Spacing();

        // Each component "teaches" the editor how to draw itself through its engine-side attributes;
        // ComponentInspector reflects over those and renders every property, so there is no per-component
        // drawing code here anymore.
        foreach (var info in ComponentInspector.ComponentTypes)
        {
            if (!Component.IsUserType(info.Type) && entity.HasComponent(info.Type))
                ComponentInspector.DrawComponent(entity, info);
        }

        // The game's own components follow, in the order they were added, then any still waiting for their
        // script (or whose script is gone).
        foreach (Component component in entity.Components.ToArray())
        {
            if (component.IsUserComponent)
                ComponentInspector.DrawUserComponent(entity, component);
        }

        if (entity.TryGetComponent(out MissingComponents? missing))
            ComponentInspector.DrawMissingComponents(entity, missing);
    }

    private static void DrawTagRow(Entity entity)
    {
        var tag = entity.GetComponent<LabelComponent>();
        bool active = tag.Enabled;
        if (ImGui.Checkbox("##Active", ref active))
            tag.Enabled = active;
        ImGui.SameLine();
        string name = tag.Name;
        ImGui.SetNextItemWidth(-1.0f);
        if (ImGui.InputText("##Tag", ref name, 256))
            tag.Name = name;
    }

    private void DrawAddComponentButton(Entity entity)
    {
        ImGui.Spacing();
        if (ImGui.Button($"{EditorIcons.Plus}  Add Component", new Vector2(-1.0f, ImGui.GetFrameHeight() + 4.0f)))
        {
            ImGui.OpenPopup("AddComponent");
            _componentSearchFilter = "";
            _namingNewComponent = false;
            _userComponentTypes = ComponentInspector.UserComponentTypes();
            ImGui.SetNextWindowFocus();
        }

        // Dropping a script from the asset browser attaches its component (pending until it compiles, if needed).
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("SCRIPT_FILE");
                if (payload.NativePtr != null
                    && System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data) is string file)
                {
                    AttachScript(entity, System.IO.Path.GetFileNameWithoutExtension(file));
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopup("AddComponent"))
        {
            if (_namingNewComponent)
                DrawNewComponentNaming(entity);
            else
                DrawComponentPicker(entity);
            ImGui.EndPopup();
        }
        else if (_userComponentTypes.Count > 0)
        {
            // Don't keep the game's types alive between openings (they are gathered afresh on open).
            _userComponentTypes = new List<Type>();
        }
    }

    private const float PickerWidth = 290f;
    private const float IconColumn = 24f;

    // The searchable menu: "New Component" first, then every addable component grouped by category (the game's
    // own under Scripts first), each with its icon. Enter adds the only match, or — when nothing matches —
    // creates a component named after the search.
    private void DrawComponentPicker(Entity entity)
    {
        // InputText captures keyboard focus and swallows Escape, so close explicitly.
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();

        ImGui.SetNextItemWidth(PickerWidth);
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        bool submitted = ImGui.InputTextWithHint("##ComponentSearch", $"{EditorIcons.Search}  Search components...",
            ref _componentSearchFilter, 256, ImGuiInputTextFlags.EnterReturnsTrue);

        string filter = _componentSearchFilter.Trim();
        List<ComponentEntry> matches = AddableComponents(entity, filter);

        // New Component comes first. With a search that matches nothing, the search itself names it: one click
        // (or Enter) creates it. Otherwise — or when that name is taken — the naming step opens, prefilled.
        string suggested = ComponentScripts.ToClassName(filter);
        bool quickCreate = suggested.Length > 0 && matches.Count == 0 && !ComponentNameTaken(suggested);
        ImGui.Spacing();
        string newLabel = quickCreate ? $"New Component \"{suggested}\"" : "New Component...";
        if (PickerRow("new", EditorIcons.Plus, EditorThemeManager.Current.Palette.Accent, newLabel, indent: 0f, keepOpen: !quickCreate)
            || (submitted && quickCreate))
        {
            if (quickCreate)
            {
                CreateUserComponent(entity, suggested);
                ImGui.CloseCurrentPopup();
            }
            else
            {
                _namingNewComponent = true;
                _focusNewComponentName = true;
                _newComponentName = suggested;
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip("Creates a C# script with a class deriving from Component and attaches it.");

        ImGui.Separator();

        ComponentEntry? chosen = null;
        // The list fits its content (every group open), up to a fixed height, so a narrowed search doesn't leave an
        // empty box. The height is worked out before drawing rather than auto-sized: ImGui places a popup once,
        // when it appears, so a popup that grew afterwards could hang off the bottom of the screen.
        var style = ImGui.GetStyle();
        int groups = matches.Select(e => e.Category).Distinct().Count();
        float content = Math.Max(matches.Count, 1) * (ImGui.GetFrameHeight() + style.ItemSpacing.Y)
            + groups * (ImGui.GetTextLineHeight() + style.FramePadding.Y * 2.0f + style.ItemSpacing.Y)
            + style.WindowPadding.Y * 2.0f;
        if (ImGui.BeginChild("ComponentList", new Vector2(PickerWidth, MathF.Min(content, 320f)), ImGuiChildFlags.None))
        {
            if (matches.Count == 0)
                ImGui.TextDisabled(filter.Length == 0 ? "Every component is already attached." : "No matching components.");

            foreach (IGrouping<string, ComponentEntry> group in matches.GroupBy(e => e.Category))
            {
                // A search shows every group open; otherwise each remembers whether it was folded.
                if (filter.Length > 0)
                    ImGui.SetNextItemOpen(true);
                if (!CategoryHeader(group.Key, group.Count()))
                    continue;

                foreach (ComponentEntry entry in group)
                {
                    if (PickerRow(entry.Type.FullName ?? entry.Type.Name, ComponentCatalog.Glyph(entry),
                            ComponentCatalog.CategoryColor(entry.Category), entry.DisplayName, ImGui.GetTreeNodeToLabelSpacing()))
                    {
                        chosen = entry;
                    }

                    if (entry.IsUser && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                        ImGui.SetTooltip(entry.Type.FullName ?? entry.Type.Name);
                }
            }

            ImGui.EndChild();
        }

        if (submitted && chosen is null && matches.Count == 1)
            chosen = matches[0];

        if (chosen is not null)
        {
            AddComponentOfType(entity, chosen.Type, chosen.DisplayName);
            ImGui.CloseCurrentPopup();
        }
    }

    // Every component the entity can still take that matches the search (by label, class name or category),
    // sorted by category, then by the engine's menu order or name.
    private List<ComponentEntry> AddableComponents(Entity entity, string filter)
    {
        var entries = new List<(ComponentEntry Entry, int Order)>();
        foreach (var info in ComponentInspector.ComponentTypes)
        {
            if (info.Addable && !Component.IsUserType(info.Type) && !entity.HasComponent(info.Type))
                entries.Add((ComponentCatalog.Describe(info.Type), info.Order));
        }

        foreach (Type type in _userComponentTypes)
        {
            if (!entity.HasComponent(type))
                entries.Add((ComponentCatalog.Describe(type), int.MaxValue));
        }

        return entries
            .Where(e => Matches(e.Entry.DisplayName, filter) || Matches(e.Entry.Type.Name, filter)
                        || Matches(e.Entry.Category, filter))
            .OrderBy(e => e.Entry.Category, Comparer<string>.Create(ComponentCatalog.CompareCategories))
            .ThenBy(e => e.Order)
            .ThenBy(e => e.Entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.Entry)
            .ToList();
    }

    // A foldable group header: the category's tinted icon and name, with its item count on the right.
    private static bool CategoryHeader(string category, int count)
    {
        var palette = EditorThemeManager.Current.Palette;
        bool open = ImGui.TreeNodeEx($"##cat_{category}",
            ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.NoTreePushOnOpen);

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        float y = min.Y + (max.Y - min.Y - ImGui.GetTextLineHeight()) * 0.5f;
        float x = min.X + ImGui.GetTreeNodeToLabelSpacing();
        var dl = ImGui.GetWindowDrawList();
        DrawIcon(dl, x, y, ComponentCatalog.CategoryGlyph(category), ComponentCatalog.CategoryColor(category));
        dl.AddText(new Vector2(x + IconColumn, y), ImGui.GetColorU32(palette.TextDisabled), category);
        string countText = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        float countWidth = ImGui.CalcTextSize(countText).X;
        dl.AddText(new Vector2(max.X - countWidth - 6.0f, y), ImGui.GetColorU32(palette.TextDisabled), countText);
        return open;
    }

    // One menu row: a tinted icon centered in its column and a label, the whole width clickable.
    // keepOpen stops ImGui from closing the popup on click, for a row that switches the popup to another step.
    private static bool PickerRow(string id, string glyph, Vector4 glyphColor, string label, float indent, bool keepOpen = false)
    {
        float height = ImGui.GetFrameHeight();
        bool clicked = ImGui.Selectable($"##{id}", false,
            keepOpen ? ImGuiSelectableFlags.DontClosePopups : ImGuiSelectableFlags.None, new Vector2(0f, height));

        Vector2 min = ImGui.GetItemRectMin();
        float y = min.Y + (height - ImGui.GetTextLineHeight()) * 0.5f;
        float x = min.X + indent;
        var dl = ImGui.GetWindowDrawList();
        DrawIcon(dl, x, y, glyph, glyphColor);
        dl.AddText(new Vector2(x + IconColumn, y), ImGui.GetColorU32(ImGuiCol.Text), label);
        return clicked;
    }

    private static void DrawIcon(ImDrawListPtr dl, float x, float y, string glyph, Vector4 color)
    {
        float width = ImGui.CalcTextSize(glyph).X;
        dl.AddText(new Vector2(x + MathF.Round((IconColumn - 6.0f - width) * 0.5f), y), ImGui.GetColorU32(color), glyph);
    }

    // Names the new component: Enter creates it, Escape goes back to the list.
    private void DrawNewComponentNaming(Entity entity)
    {
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _namingNewComponent = false;
            return;
        }

        ImGui.TextUnformatted("New Component");
        ImGui.SetNextItemWidth(250f);
        if (_focusNewComponentName)
        {
            ImGui.SetKeyboardFocusHere();
            _focusNewComponentName = false;
        }
        bool submitted = ImGui.InputTextWithHint("##NewComponentName", "PlayerMovement", ref _newComponentName, 128,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CharsNoBlank);

        string name = _newComponentName.Trim();
        bool exists = name.Length > 0 && ComponentNameTaken(name);
        bool valid = ComponentScripts.IsValidClassName(name) && !exists;
        if (name.Length > 0 && !valid)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.70f, 0.25f, 1.0f),
                exists ? "A script with this name already exists." : "Use letters, digits and _ (start with a letter).");
        }
        else
        {
            ImGui.TextDisabled("Creates Assets/Scripts/" + (name.Length > 0 ? name : "<Name>") + ".cs");
        }

        ImGui.BeginDisabled(!valid);
        bool create = ImGui.Button("Create", new Vector2(122f, 0f)) || (submitted && valid);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(122f, 0f)))
            _namingNewComponent = false;

        if (create)
        {
            CreateUserComponent(entity, name);
            _namingNewComponent = false;
            ImGui.CloseCurrentPopup();
        }
    }

    // Whether a component or script by this name already exists, so a new one would clash with it.
    private bool ComponentNameTaken(string name) =>
        _userComponentTypes.Exists(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
        || EditorGui.ScriptExists(name);

    private static bool Matches(string name, string filter) =>
        filter.Length == 0 || name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static void AddComponentOfType(Entity entity, Type type, string displayName)
    {
        try
        {
            var component = (Component)Activator.CreateInstance(type)!;

            // A new 3D collider starts out matching the entity's mesh, as it would be sized by hand.
            if (component is Spot.Engine.Physics.Collider3DComponent collider
                && entity.TryGetComponent(out MeshComponent? mesh) && mesh is not null)
            {
                Spot.Engine.Physics.ColliderFitting.FitToMesh(collider, mesh);
            }

            entity.AddComponent(component);
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Error("Failed to add component '{0}': {1}", displayName, ex.Message);
        }
    }

    private static void AddUserComponent(Entity entity, Type type)
    {
        try
        {
            entity.AddComponent((Component)Activator.CreateInstance(type)!);
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Error("Failed to add component '{0}': {1}", type.Name, ex.Message);
        }
    }

    // Attaches the component a script file defines: directly when its type is loaded, otherwise as a pending
    // reference (by the script's guid) that resolves once the scripts compile.
    private static void AttachScript(Entity entity, string className)
    {
        Type? type = ComponentInspector.UserComponentTypes()
            .Find(t => string.Equals(t.Name, className, StringComparison.OrdinalIgnoreCase));
        if (type is not null)
        {
            if (!entity.HasComponent(type))
                AddUserComponent(entity, type);
            return;
        }

        AttachPending(entity, className, EditorGui.GetOrCreateScriptGuid(className));
    }

    private static void AttachPending(Entity entity, string className, string guid)
    {
        var data = new System.Text.Json.Nodes.JsonObject { ["Type"] = className };
        if (guid.Length > 0)
            data["Guid"] = guid;

        if (!entity.TryGetComponent(out MissingComponents? pending))
            pending = entity.AddComponent(new MissingComponents());
        pending.Items.Add(new MissingComponent(data));
    }

    // Writes the new script and attaches it straight away as a pending component: the entity holds its
    // reference (by guid) until the scripts recompile, at which point it resolves into the real component.
    private static void CreateUserComponent(Entity entity, string name)
    {
        if (!ComponentScripts.Create(name, out string path, out string guid))
            return;

        AttachPending(entity, name, guid);
        Spot.Engine.Log.Info("Created {0}. {1} attaches once the scripts compile.", path, name);
        OpenScript(path);
    }

    // Opens the new script in the system's editor for .cs files, so the next step — writing the component —
    // is one keystroke away. A machine without an association simply keeps the file closed.
    private static void OpenScript(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Warn("Could not open '{0}': {1}", path, ex.Message);
        }
    }

    // ----- Prefab editor ---------------------------------------------------------------------------

    private void DrawPrefabEditor(string path)
    {
        // Load (or reload) the prefab into an isolated scene the first time this path is inspected. Its
        // scripts are resolved but never ticked, so nothing runs — we only present and edit its components.
        if (path != _prefabPath || _prefabRoot == null)
        {
            _prefabScene = new Scene();
            _prefabRoot = Prefab.InstantiateFile(_prefabScene, path, null);
            _prefabPath = path;
            _prefabLastJson = _prefabRoot != null ? Prefab.Serialize(_prefabRoot.Value) : "";
        }

        ImGui.TextDisabled(System.IO.Path.GetFileName(path));
        ImGui.Separator();

        if (_prefabRoot == null)
        {
            ImGui.TextDisabled("This prefab could not be loaded.");
            return;
        }

        Entity root = _prefabRoot.Value;

        // The prefab is inspected inside a throwaway Scene, so the generic property editing below must
        // not record undo entries: they would target entities that vanish the moment this panel
        // reloads the prefab. The file write underneath is the prefab's own persistence.
        Entity prefabRootEntity = root;
        using (Spot.DebugUI.Undo.EditorHistory.Current.Suspend())
        {
            DrawComponents(prefabRootEntity);
            ImGui.Spacing();
            DrawAddComponentButton(prefabRootEntity);
        }

        // Persist edits when the user releases a control, so we don't write to disk every frame while dragging.
        string current = Prefab.Serialize(root);
        if (current != _prefabLastJson && !ImGui.IsAnyItemActive())
        {
            try
            {
                System.IO.File.WriteAllText(path, current);
                _prefabLastJson = current;
            }
            catch (Exception ex)
            {
                Spot.Engine.Log.Error("Failed to save prefab '{0}': {1}", path, ex.Message);
            }
        }
    }

    // ----- Built-in assets -------------------------------------------------------------------------

    // A read-only view of a built-in asset: a preview, what it is, its reference, and a way to get an editable
    // copy into the project.
    private void DrawBuiltinAsset(BuiltinAsset asset)
    {
        string kind = asset.Kind switch
        {
            BuiltinAssetKind.Mesh => "Built-in mesh",
            BuiltinAssetKind.Texture => "Built-in texture",
            _ => "Built-in material",
        };
        ImGui.TextUnformatted(asset.Name);
        ImGui.SameLine();
        ImGui.TextDisabled(kind);
        ImGui.Separator();

        const float previewSize = 200.0f;
        float xOffset = (ImGui.GetContentRegionAvail().X - previewSize) * 0.5f;
        if (xOffset > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + xOffset);
        nint preview = BuiltinPreview(asset);
        if (preview != 0)
            ImGui.Image(preview, new Vector2(previewSize, previewSize), new Vector2(0, 1), new Vector2(1, 0));
        else
            ImGui.Dummy(new Vector2(previewSize, previewSize));
        ImGui.Separator();

        ImGui.TextWrapped(asset.Description);
        ImGui.Spacing();
        ImGui.TextDisabled(asset.Reference);
        ImGui.Spacing();
        ImGui.TextWrapped("Built-in assets are shared and read-only. Copy one into the project to edit it.");
        ImGui.Spacing();

        if (ImGui.Button($"{EditorIcons.FolderOpen}  Copy to Project", new Vector2(-1, 0)))
            CopyBuiltinToProject(asset);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Saves an editable copy into the project's Assets folder and selects it.");

        if (ImGui.Button("Copy Reference", new Vector2(-1, 0)))
            ImGui.SetClipboardText(asset.Reference);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copies the reference, for use from scripts.");
    }

    // The preview texture for a built-in: the texture itself, a material sphere, or a rendered mesh.
    private nint BuiltinPreview(BuiltinAsset asset)
    {
        try
        {
            switch (asset.Kind)
            {
                case BuiltinAssetKind.Texture:
                    return (nint)BuiltinAssets.LoadTexture(asset.Reference).Handle.Id;

                case BuiltinAssetKind.Material:
                    _materialPreviewFb ??= new Spot.Engine.Graphics.Framebuffer(200, 200);
                    Material material = BuiltinAssets.LoadMaterial(asset.Reference);
                    int signature = MaterialPreviewHelper.Signature(material);
                    if (_materialPreviewPath != asset.Reference || _materialPreviewSig != signature)
                    {
                        MaterialPreviewHelper.RenderToFramebuffer(material, _materialPreviewFb);
                        _materialPreviewPath = asset.Reference;
                        _materialPreviewSig = signature;
                    }

                    return (nint)_materialPreviewFb.ColorAttachment;

                default:
                    _builtinPreviewFb ??= new Spot.Engine.Graphics.Framebuffer(200, 200);
                    if (_builtinPreviewReference != asset.Reference)
                    {
                        ModelPreviewHelper.RenderToFramebuffer(BuiltinAssets.LoadModel(asset.Reference), _builtinPreviewFb);
                        _builtinPreviewReference = asset.Reference;
                    }

                    return (nint)_builtinPreviewFb.ColorAttachment;
            }
        }
        catch (Exception ex)
        {
            if (_materialPreviewErrorPath != asset.Reference)
            {
                _materialPreviewErrorPath = asset.Reference;
                Spot.Engine.Log.Error("Failed to preview '{0}': {1}", asset.Reference, ex.Message);
            }

            return 0;
        }
    }

    // Saves an editable copy into the project's Assets folder and selects it, so a copied material opens straight
    // into the material editor.
    private void CopyBuiltinToProject(BuiltinAsset asset)
    {
        try
        {
            string folder = string.IsNullOrEmpty(AssetPath.Root) ? Environment.CurrentDirectory : AssetPath.Root;
            string path = BuiltinAssets.Export(asset.Reference, folder);
            Spot.Engine.Log.Info("Copied built-in '{0}' to {1}.", asset.Name, path);
            _context.SelectedAssetPath = path;
        }
        catch (Exception ex)
        {
            Spot.Engine.Log.Error("Failed to copy '{0}' into the project: {1}", asset.Name, ex.Message);
        }
    }

    // ----- Material editor -------------------------------------------------------------------------

    private void DrawMaterialEditor(string path)
    {
        // Material.Load caches by path and never throws (it logs and returns a default on failure),
        // so editing this instance updates every model referencing the same file live.
        var material = Material.Load(path);

        ImGui.TextDisabled(System.IO.Path.GetFileName(path));
        ImGui.Separator();

        // Draw preview
        uint previewSize = 200;
        if (_materialPreviewFb == null)
        {
            _materialPreviewFb = new Spot.Engine.Graphics.Framebuffer(previewSize, previewSize);
        }
        
        // Rendering the preview is a full offscreen draw, so only do it when the selected material or one of
        // its preview-relevant properties actually changed — not every frame the inspector is open. Edits
        // below mutate the material this frame and are picked up on the next (an imperceptible one-frame lag).
        int previewSig = MaterialPreviewHelper.Signature(material);
        if (_materialPreviewPath != path || _materialPreviewSig != previewSig)
        {
            // A faulty material/shader must not throw out of the panel. Render defensively; on failure keep
            // whatever was last in the buffer and log once for this material.
            try
            {
                MaterialPreviewHelper.RenderToFramebuffer(material, _materialPreviewFb);
                _materialPreviewPath = path;
                _materialPreviewSig = previewSig;
                if (_materialPreviewErrorPath == path) _materialPreviewErrorPath = null;
            }
            catch (Exception ex)
            {
                if (_materialPreviewErrorPath != path)
                {
                    _materialPreviewErrorPath = path;
                    Spot.Engine.Log.Error("Failed to render material preview for '{0}': {1}", path, ex.Message);
                }
            }
        }

        float availX = ImGui.GetContentRegionAvail().X;
        float xOffset = (availX - previewSize) * 0.5f;
        if (xOffset > 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + xOffset);
        }
        
        ImGui.Image((IntPtr)_materialPreviewFb.ColorAttachment, new Vector2(previewSize, previewSize), new Vector2(0, 1), new Vector2(1, 0));
        ImGui.Separator();

        var color = material.Color;
        if (EditorGui.Color4("Color", ref color))
            material.Color = color;
        if (ImGui.IsItemDeactivatedAfterEdit())
            material.Save(path);

        string[] shaderTypes = Enum.GetNames(typeof(MaterialShaderType));
        int currentShader = (int)material.ShaderType;
        if (EditorGui.Combo("Shader Type", ref currentShader, shaderTypes))
        {
            material.ShaderType = (MaterialShaderType)currentShader;
            material.Save(path);
        }

        if (material.ShaderType == MaterialShaderType.Standard)
        {
            float metallic = material.Metallic;
            if (EditorGui.DragFloat("Metallic", ref metallic, 0.01f, 0.0f, 1.0f))
            {
                material.Metallic = metallic;
                material.Save(path);
            }

            bool emissiveChanged = false;
            var emissive = material.EmissiveColor;
            if (EditorGui.Color3("Emissive", ref emissive)) { material.EmissiveColor = emissive; emissiveChanged = true; }
            float emissiveIntensity = material.EmissiveIntensity;
            // Allow > 1 so a surface can glow into HDR and drive bloom.
            if (EditorGui.DragFloat("Emissive Intensity", ref emissiveIntensity, 0.05f, 0.0f, 20.0f)) { material.EmissiveIntensity = emissiveIntensity; emissiveChanged = true; }
            if (emissiveChanged) material.Save(path);
        }
        else if (material.ShaderType == MaterialShaderType.Water)
        {
            bool waterChanged = false;
            float waveSpeed = material.WaveSpeed;
            if (EditorGui.DragFloat("Wave Speed", ref waveSpeed, 0.01f, 0.0f, 10.0f)) { material.WaveSpeed = waveSpeed; waterChanged = true; }
            float waveScale = material.WaveScale;
            if (EditorGui.DragFloat("Wave Scale", ref waveScale, 0.01f, 0.0f, 10.0f)) { material.WaveScale = waveScale; waterChanged = true; }
            float waveStrength = material.WaveStrength;
            if (EditorGui.DragFloat("Wave Strength", ref waveStrength, 0.01f, 0.0f, 5.0f)) { material.WaveStrength = waveStrength; waterChanged = true; }
            float specPower = material.SpecularPower;
            if (EditorGui.DragFloat("Specular Power", ref specPower, 1.0f, 1.0f, 512.0f)) { material.SpecularPower = specPower; waterChanged = true; }

            if (waterChanged) material.Save(path);
        }

        ImGui.Spacing();
        bool changedTiling = false;
        var tiling = material.Tiling;
        if (EditorGui.Vector2Control("Tiling", ref tiling, resetValue: 1.0f)) { material.Tiling = tiling; changedTiling = true; }

        bool autoTile = material.AutoTile;
        if (EditorGui.Checkbox("Auto Tile (Repeat)", ref autoTile)) { material.AutoTile = autoTile; changedTiling = true; }

        if (changedTiling) material.Save(path);

        ImGui.Spacing();

        // Texture and normal-map slots share the reusable AssetSlot widget: drag an image in, or click to
        // pick one from a searchable, thumbnailed list; the ✕ clears the slot. Each edit saves immediately.
        string[] imagePatterns = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.bmp" };

        if (EditorGui.AssetSlot("Texture", "IMAGE_FILE", imagePatterns, material.TexturePath, out string? newTexture, builtins: BuiltinAssetKind.Texture))
        {
            try
            {
                material.SetTexture(newTexture);
                material.Save(path);
            }
            catch (Exception ex)
            {
                Spot.Engine.Log.Error("Failed to set material texture: {0}", ex.Message);
            }
        }

        if (EditorGui.AssetSlot("Normal Map", "IMAGE_FILE", imagePatterns, material.NormalMapPath, out string? newNormal, builtins: BuiltinAssetKind.Texture))
        {
            try
            {
                material.SetNormalMap(newNormal);
                material.Save(path);
            }
            catch (Exception ex)
            {
                Spot.Engine.Log.Error("Failed to set material normal map: {0}", ex.Message);
            }
        }
    }
}
