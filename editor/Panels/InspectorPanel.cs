using System;
using System.Numerics;
using ImGuiNET;
using Spot.Scenes;
using Spot.Assets;
using Spot.Editor.UI;

namespace Spot.Editor.Panels;

public class InspectorPanel
{
    private readonly EditorContext _context;
    private Spot.Rendering.Framebuffer? _materialPreviewFb;

    public InspectorPanel(EditorContext context)
    {
        _context = context;
    }

    public void OnImGuiRender(ref bool open)
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse;
        ImGui.Begin("Properties", ref open, flags);

        if (_context.SelectedAssetPath != null && _context.SelectedAssetPath.EndsWith(".sptmat", StringComparison.OrdinalIgnoreCase))
        {
            DrawMaterialEditor(_context.SelectedAssetPath);
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
            ImGui.OpenPopup("AddComponent");

        if (ImGui.BeginPopup("AddComponent"))
        {
            // The menu is built from the same discovered component list as the inspector: any component
            // marked addable that the entity doesn't already have.
            foreach (var info in ComponentInspector.ComponentTypes)
            {
                if (!info.Addable || entity.HasComponent(info.Type))
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
                        Spot.Core.Log.Error("Failed to add component '{0}': {1}", info.DisplayName, ex.Message);
                    }
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
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
            _materialPreviewFb = new Spot.Rendering.Framebuffer(previewSize, previewSize);
        }
        
        Spot.Editor.UI.MaterialPreviewHelper.RenderToFramebuffer(material, _materialPreviewFb);
        
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

        if (EditorGui.AssetSlot("Texture", "IMAGE_FILE", imagePatterns, material.TexturePath, out string? newTexture))
        {
            try
            {
                material.SetTexture(newTexture);
                material.Save(path);
            }
            catch (Exception ex)
            {
                Spot.Core.Log.Error("Failed to set material texture: {0}", ex.Message);
            }
        }

        if (EditorGui.AssetSlot("Normal Map", "IMAGE_FILE", imagePatterns, material.NormalMapPath, out string? newNormal))
        {
            try
            {
                material.SetNormalMap(newNormal);
                material.Save(path);
            }
            catch (Exception ex)
            {
                Spot.Core.Log.Error("Failed to set material normal map: {0}", ex.Message);
            }
        }
    }
}
