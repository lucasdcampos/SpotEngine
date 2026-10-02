using System;
using System.Numerics;
using ImGuiNET;
using Spot.Engine.Assets;
using Spot.Engine.Scenes;
using Spot.Framework.Graphics;
using Spot.DebugUI.UI;

namespace Spot.DebugUI.Panels;

public class InspectorPanel : IDisposable
{
    private readonly ISelectionContext _context;
    private Spot.Framework.Graphics.Framebuffer? _materialPreviewFb;
    // The material path and property fingerprint the preview framebuffer was last rendered for, so the
    // offscreen render only re-runs when something the preview shows actually changed.
    private string? _materialPreviewPath;
    private int _materialPreviewSig;
    // The material path we last logged a preview-render failure for, so a broken material logs once instead
    // of every frame the inspector is open.
    private string? _materialPreviewErrorPath;

    // The preview of the selected built-in mesh (rendered once per selection).
    private Spot.Framework.Graphics.Framebuffer? _builtinPreviewFb;
    private string? _builtinPreviewReference;

    // Prefab editing state: the inspected prefab is loaded into an isolated scene so its components can be
    // edited with the same reflection-based UI as a live entity, then re-serialized back to disk on change.
    private Scene? _prefabScene;
    private Entity? _prefabRoot;
    private string? _prefabPath;
    private string _prefabLastJson = "";
    private string _componentSearchFilter = "";

    public InspectorPanel(ISelectionContext context)
    {
        _context = context;
    }

    // Releases the material-preview framebuffer's GL resources. Called when the editor shuts down.
    public void Dispose()
    {
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
            if (entity.HasComponent(info.Type))
                ComponentInspector.DrawComponent(entity, info);
        }
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
        if (ImGui.Button("Add Component", new Vector2(-1.0f, 0.0f)))
        {
            ImGui.OpenPopup("AddComponent");
            _componentSearchFilter = "";
            ImGui.SetNextWindowFocus();
        }

        if (ImGui.BeginPopup("AddComponent"))
        {
            // InputText captures keyboard focus and swallows Escape, so close explicitly.
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                ImGui.CloseCurrentPopup();

            ImGui.SetNextItemWidth(250f);
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();
            ImGui.InputTextWithHint("##ComponentSearch", "Search...", ref _componentSearchFilter, 256);
            
            ImGui.Spacing();

            if (ImGui.BeginChild("ComponentList", new Vector2(250f, 300f), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                // The menu is built from the same discovered component list as the inspector: any component
                // marked addable that the entity doesn't already have.
                foreach (var info in ComponentInspector.ComponentTypes)
                {
                    if (!info.Addable || entity.HasComponent(info.Type))
                        continue;

                    if (!string.IsNullOrEmpty(_componentSearchFilter) && !info.DisplayName.Contains(_componentSearchFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ImGui.MenuItem(info.DisplayName))
                    {
                        try
                        {
                            var component = (Component)Activator.CreateInstance(info.Type)!;
                            entity.AddComponent(component);
                        }
                        catch (Exception ex)
                        {
                            Spot.Framework.Log.Error("Failed to add component '{0}': {1}", info.DisplayName, ex.Message);
                        }
                        ImGui.CloseCurrentPopup();
                    }
                }
                ImGui.EndChild();
            }
            ImGui.EndPopup();
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
                Spot.Framework.Log.Error("Failed to save prefab '{0}': {1}", path, ex.Message);
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
                    _materialPreviewFb ??= new Spot.Framework.Graphics.Framebuffer(200, 200);
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
                    _builtinPreviewFb ??= new Spot.Framework.Graphics.Framebuffer(200, 200);
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
                Spot.Framework.Log.Error("Failed to preview '{0}': {1}", asset.Reference, ex.Message);
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
            string folder = Spot.Engine.Project.Active?.GetAssetDirectory() ?? Environment.CurrentDirectory;
            string path = BuiltinAssets.Export(asset.Reference, folder);
            Spot.Framework.Log.Info("Copied built-in '{0}' to {1}.", asset.Name, path);
            _context.SelectedAssetPath = path;
        }
        catch (Exception ex)
        {
            Spot.Framework.Log.Error("Failed to copy '{0}' into the project: {1}", asset.Name, ex.Message);
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
            _materialPreviewFb = new Spot.Framework.Graphics.Framebuffer(previewSize, previewSize);
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
                    Spot.Framework.Log.Error("Failed to render material preview for '{0}': {1}", path, ex.Message);
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
                Spot.Framework.Log.Error("Failed to set material texture: {0}", ex.Message);
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
                Spot.Framework.Log.Error("Failed to set material normal map: {0}", ex.Message);
            }
        }
    }
}
