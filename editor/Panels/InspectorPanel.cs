using System;
using System.Numerics;
using ImGuiNET;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Physics;
using Spot.Assets;
using Spot.Editor.UI;

namespace Spot.Editor.Panels;

public class InspectorPanel
{
    private readonly EditorContext _context;

    public InspectorPanel(EditorContext context)
    {
        _context = context;
    }

    public void OnImGuiRender(ref bool open)
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse;
        ImGui.Begin("Inspector", ref open, flags);

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

        EditorGui.Component<Transform>(entity, "Transform", t =>
        {
            var position = t.Position;
            if (EditorGui.Vector3Control("Position", ref position)) t.Position = position;
            var rotation = t.Rotation;
            if (EditorGui.Vector3Control("Rotation", ref rotation)) t.Rotation = rotation;
            var scale = t.Scale;
            if (EditorGui.Vector3Control("Scale", ref scale, resetValue: 1.0f)) t.Scale = scale;
        }, removable: false);

        EditorGui.Component<Sprite2D>(entity, "Sprite2D", DrawSprite);
        EditorGui.Component<MeshRenderer>(entity, "Mesh Renderer", DrawMeshRenderer);
        EditorGui.Component<CameraComponent>(entity, "Camera", DrawCamera);
        EditorGui.Component<PhysicsBody2DComponent>(entity, "Physics Body 2D", DrawPhysicsBody);
        EditorGui.Component<BoxCollider2DComponent>(entity, "Box Collider 2D", DrawBoxCollider);
        EditorGui.Component<PhysicsBody3DComponent>(entity, "Physics Body 3D", DrawPhysicsBody3D);
        EditorGui.Component<BoxCollider3DComponent>(entity, "Box Collider 3D", DrawBoxCollider3D);
        EditorGui.Component<LightComponent>(entity, "Light", DrawLight);
        EditorGui.Component<DynamicCloudsComponent>(entity, "Dynamic Clouds", DrawDynamicClouds);
        EditorGui.Component<ScriptComponent>(entity, "Script Component", DrawScripts);
        EditorGui.Component<PostProcessingComponent>(entity, "Post Processing", DrawPostProcessing);
    }

    private static void DrawTagRow(Entity entity)
    {
        var tag = entity.GetComponent<TagComponent>();
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
            EditorGui.AddComponentItem<Sprite2D>(entity, "Sprite2D");
            EditorGui.AddComponentItem<MeshRenderer>(entity, "Mesh Renderer");
            EditorGui.AddComponentItem<CameraComponent>(entity, "Camera");
            EditorGui.AddComponentItem<PhysicsBody2DComponent>(entity, "Physics Body 2D");
            EditorGui.AddComponentItem<BoxCollider2DComponent>(entity, "Box Collider 2D");
            EditorGui.AddComponentItem<PhysicsBody3DComponent>(entity, "Physics Body 3D");
            EditorGui.AddComponentItem<BoxCollider3DComponent>(entity, "Box Collider 3D");
            EditorGui.AddComponentItem<LightComponent>(entity, "Light");
            EditorGui.AddComponentItem<DynamicCloudsComponent>(entity, "Dynamic Clouds");
            EditorGui.AddComponentItem<ScriptComponent>(entity, "Script Component");
            EditorGui.AddComponentItem<PostProcessingComponent>(entity, "Post Processing");
            ImGui.EndPopup();
        }
    }

    // ----- Per-component bodies --------------------------------------------------------------------

    private static void DrawSprite(Sprite2D sprite)
    {
        bool enabled = sprite.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) sprite.Enabled = enabled;
        var color = sprite.Color;
        if (EditorGui.Color4("Color", ref color)) sprite.Color = color;

        ImGui.Button(sprite.Texture != null ? "Texture Loaded (Drop to change)" : "Drop Texture Here", new Vector2(-1, 30));
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("IMAGE_FILE");
                if (payload.NativePtr != null)
                {
                    string? filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                    if (filepath != null)
                    {
                        try
                        {
                            var newTexture = new Texture2D(filepath);
                            sprite.Texture?.Dispose();
                            sprite.Texture = newTexture;
                            sprite.TexturePath = filepath;
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine($"Failed to load texture: {ex.Message}");
                        }
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (sprite.Texture != null)
        {
            if (ImGui.Button("Remove Texture", new Vector2(-1, 24)))
            {
                sprite.Texture.Dispose();
                sprite.Texture = null;
                sprite.TexturePath = null;
            }
        }
    }

    private static void DrawMeshRenderer(MeshRenderer meshRenderer)
    {
        bool enabled = meshRenderer.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) meshRenderer.Enabled = enabled;
        var color = meshRenderer.Color;
        if (EditorGui.Color4("Color", ref color)) meshRenderer.Color = color;

        string modelLabel = meshRenderer.Model != null
            ? $"{System.IO.Path.GetFileName(meshRenderer.ModelPath) ?? "Model"} (Drop to change)"
            : "Drop 3D Model Here";
        bool modelClicked = ImGui.Button(modelLabel, new Vector2(-1, 30));
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("MODEL_FILE");
                if (payload.NativePtr != null)
                {
                    string? filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                    if (filepath != null)
                    {
                        try
                        {
                            meshRenderer.Model = Model.Load(filepath);
                            meshRenderer.ModelPath = filepath;
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine($"Failed to load model: {ex.Message}");
                        }
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (modelClicked)
            ImGui.OpenPopup("SelectModelPopup");
        if (ImGui.BeginPopup("SelectModelPopup"))
        {
            if (ImGui.MenuItem("None", "", meshRenderer.Model == null))
            {
                meshRenderer.Model = null;
                meshRenderer.ModelPath = null;
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Cube", "", meshRenderer.ModelPath == "primitive:Cube")) { meshRenderer.ModelPath = "primitive:Cube"; meshRenderer.Model = Model.Load("primitive:Cube"); }
            if (ImGui.MenuItem("Plane", "", meshRenderer.ModelPath == "primitive:Plane")) { meshRenderer.ModelPath = "primitive:Plane"; meshRenderer.Model = Model.Load("primitive:Plane"); }
            if (ImGui.MenuItem("Quad", "", meshRenderer.ModelPath == "primitive:Quad")) { meshRenderer.ModelPath = "primitive:Quad"; meshRenderer.Model = Model.Load("primitive:Quad"); }
            if (ImGui.MenuItem("Sphere", "", meshRenderer.ModelPath == "primitive:Sphere")) { meshRenderer.ModelPath = "primitive:Sphere"; meshRenderer.Model = Model.Load("primitive:Sphere"); }
            ImGui.EndPopup();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Material");
        string materialLabel = meshRenderer.Material != null
            ? System.IO.Path.GetFileName(meshRenderer.MaterialPath) ?? "Material"
            : "Select Material...";
        bool materialClicked = ImGui.Button(materialLabel, new Vector2(-1, 30));

        // Also accept a material dragged from the Asset Browser.
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("MATERIAL_FILE");
                if (payload.NativePtr != null)
                {
                    string? filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                    if (filepath != null)
                    {
                        AssignMaterial(meshRenderer, filepath);
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        // Clicking the slot opens a picker listing every material asset in the project.
        if (materialClicked)
            ImGui.OpenPopup("SelectMaterialPopup");
        if (ImGui.BeginPopup("SelectMaterialPopup"))
        {
            if (ImGui.MenuItem("None", "", meshRenderer.Material == null))
            {
                meshRenderer.Material = null;
                meshRenderer.MaterialPath = null;
            }
            if (ImGui.MenuItem("Checkerboard", "", meshRenderer.MaterialPath == "editor:Checkerboard"))
            {
                AssignMaterial(meshRenderer, "editor:Checkerboard");
            }

            var materials = EnumerateProjectMaterials();
            if (materials.Count > 0)
                ImGui.Separator();
            foreach (var matPath in materials)
            {
                bool isSelected = string.Equals(meshRenderer.MaterialPath, matPath, StringComparison.OrdinalIgnoreCase);
                if (ImGui.MenuItem(System.IO.Path.GetFileName(matPath), "", isSelected))
                    AssignMaterial(meshRenderer, matPath);
            }
            if (materials.Count == 0)
                ImGui.TextDisabled("No materials in project.");

            ImGui.EndPopup();
        }
    }

    private static void DrawCamera(CameraComponent cameraComp)
    {
        bool enabled = cameraComp.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) cameraComp.Enabled = enabled;
        bool primary = cameraComp.Primary;
        if (EditorGui.Checkbox("Primary", ref primary)) cameraComp.Primary = primary;

        string[] projectionTypes = { "Orthographic", "Perspective" };
        int currentProjection = (int)cameraComp.ProjectionType;
        if (EditorGui.Combo("Projection", ref currentProjection, projectionTypes))
            cameraComp.ProjectionType = (SceneCameraProjection)currentProjection;

        if (cameraComp.ProjectionType == SceneCameraProjection.Perspective)
        {
            float fov = cameraComp.FieldOfView;
            if (EditorGui.DragFloat("Field Of View", ref fov, 1.0f, 1.0f, 180.0f)) cameraComp.FieldOfView = fov;
        }
        else
        {
            float zoom = cameraComp.ZoomLevel;
            if (EditorGui.DragFloat("Zoom Level", ref zoom, 0.1f, 0.1f, 100.0f)) cameraComp.ZoomLevel = zoom;
        }

        bool fixedAspect = cameraComp.FixedAspectRatio;
        if (EditorGui.Checkbox("Fixed Aspect Ratio", ref fixedAspect)) cameraComp.FixedAspectRatio = fixedAspect;

        var bgColor = cameraComp.BackgroundColor;
        if (EditorGui.Color4("Background", ref bgColor)) cameraComp.BackgroundColor = bgColor;
    }

    private static void DrawPhysicsBody(PhysicsBody2DComponent body)
    {
        bool enabled = body.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) body.Enabled = enabled;
        var velocity = body.Velocity;
        if (EditorGui.Vector2Control("Velocity", ref velocity)) body.Velocity = velocity;
        float gravity = body.GravityScale;
        if (EditorGui.DragFloat("Gravity Scale", ref gravity)) body.GravityScale = gravity;
        bool isDynamic = body.IsDynamic;
        if (EditorGui.Checkbox("Is Dynamic", ref isDynamic)) body.IsDynamic = isDynamic;
    }

    private static void DrawBoxCollider(BoxCollider2DComponent collider)
    {
        bool enabled = collider.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) collider.Enabled = enabled;
        var size = collider.Size;
        if (EditorGui.Vector2Control("Size", ref size)) collider.Size = size;
        var offset = collider.Offset;
        if (EditorGui.Vector2Control("Offset", ref offset)) collider.Offset = offset;
    }

    private static void DrawPhysicsBody3D(PhysicsBody3DComponent body)
    {
        bool enabled = body.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) body.Enabled = enabled;
        var velocity = body.Velocity;
        if (EditorGui.Vector3Control("Velocity", ref velocity)) body.Velocity = velocity;
        float gravity = body.GravityScale;
        if (EditorGui.DragFloat("Gravity Scale", ref gravity)) body.GravityScale = gravity;
        bool isDynamic = body.IsDynamic;
        if (EditorGui.Checkbox("Is Dynamic", ref isDynamic)) body.IsDynamic = isDynamic;
    }

    private static void DrawBoxCollider3D(BoxCollider3DComponent collider)
    {
        bool enabled = collider.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) collider.Enabled = enabled;
        var size = collider.Size;
        if (EditorGui.Vector3Control("Size", ref size)) collider.Size = size;
        var offset = collider.Offset;
        if (EditorGui.Vector3Control("Offset", ref offset)) collider.Offset = offset;
    }

    private static void DrawLight(LightComponent light)
    {
        bool enabled = light.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) light.Enabled = enabled;
        
        string[] lightTypes = Enum.GetNames(typeof(LightType));
        int currentType = (int)light.Type;
        if (EditorGui.Combo("Type", ref currentType, lightTypes))
            light.Type = (LightType)currentType;
            
        var color = light.Color;
        if (EditorGui.Color3("Color", ref color)) light.Color = color;
        float intensity = light.Intensity;
        if (EditorGui.DragFloat("Intensity", ref intensity, 0.05f, 0.0f, 10.0f)) light.Intensity = intensity;
        
        if (light.Type == LightType.Directional)
        {
            float ambientIntensity = light.AmbientIntensity;
            if (EditorGui.DragFloat("Ambient Intensity", ref ambientIntensity, 0.01f, 0.0f, 1.0f)) light.AmbientIntensity = ambientIntensity;
            bool castShadows = light.CastShadows;
            if (EditorGui.Checkbox("Cast Shadows", ref castShadows)) light.CastShadows = castShadows;
        }
        else if (light.Type == LightType.Point)
        {
            float range = light.Range;
            if (EditorGui.DragFloat("Range", ref range, 0.1f, 0.0f, 100.0f)) light.Range = range;
        }
    }

    private static void DrawDynamicClouds(DynamicCloudsComponent clouds)
    {
        bool enabled = clouds.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) clouds.Enabled = enabled;
        var topColor = clouds.ColorTop;
        if (EditorGui.Color3("Color Top", ref topColor)) clouds.ColorTop = topColor;
        var botColor = clouds.ColorBottom;
        if (EditorGui.Color3("Color Bottom", ref botColor)) clouds.ColorBottom = botColor;
        float speed = clouds.Speed;
        if (EditorGui.DragFloat("Speed", ref speed, 0.01f, 0.0f, 10.0f)) clouds.Speed = speed;
        float density = clouds.Density;
        if (EditorGui.DragFloat("Density", ref density, 0.01f, 0.0f, 1.0f)) clouds.Density = density;
        float height = clouds.Height;
        if (EditorGui.DragFloat("Height", ref height, 0.01f, 0.0f, 10.0f)) clouds.Height = height;
        float opacity = clouds.Opacity;
        if (EditorGui.DragFloat("Opacity", ref opacity, 0.01f, 0.0f, 1.0f)) clouds.Opacity = opacity;
        float volume = clouds.Volume;
        if (EditorGui.DragFloat("Volume", ref volume, 0.01f, 0.0f, 5.0f)) clouds.Volume = volume;
    }

    private static void DrawScripts(ScriptComponent scriptComp)
    {
        bool enabled = scriptComp.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) scriptComp.Enabled = enabled;

        int scriptToRemove = -1;
        for (int i = 0; i < scriptComp.ClassNames.Count; i++)
        {
            string className = scriptComp.ClassNames[i];
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 30.0f);
            if (ImGui.InputText($"##Script{i}", ref className, 256))
                scriptComp.ClassNames[i] = className;
            ImGui.SameLine();
            if (ImGui.Button($"X##{i}"))
                scriptToRemove = i;
        }

        if (scriptToRemove >= 0)
            scriptComp.ClassNames.RemoveAt(scriptToRemove);

        ImGui.Button("Drop Script Here", new Vector2(-1, 30));
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("SCRIPT_FILE");
                if (payload.NativePtr != null)
                {
                    string? filename = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                    if (filename != null && filename.EndsWith(".cs"))
                    {
                        string cName = filename.Substring(0, filename.Length - 3);
                        if (!scriptComp.ClassNames.Contains(cName))
                            scriptComp.ClassNames.Add(cName);
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }
    }

    private static void DrawPostProcessing(PostProcessingComponent pp)
    {
        bool enabled = pp.Enabled;
        if (EditorGui.Checkbox("Enabled", ref enabled)) pp.Enabled = enabled;
        
        float exposure = pp.Exposure;
        if (EditorGui.DragFloat("Exposure", ref exposure, 0.05f, 0.0f, 10.0f)) pp.Exposure = exposure;
        
        float gamma = pp.Gamma;
        if (EditorGui.DragFloat("Gamma", ref gamma, 0.05f, 0.0f, 10.0f)) pp.Gamma = gamma;
        
        bool enableVignette = pp.EnableVignette;
        if (EditorGui.Checkbox("Enable Vignette", ref enableVignette)) pp.EnableVignette = enableVignette;
        
        if (pp.EnableVignette)
        {
            float intensity = pp.VignetteIntensity;
            if (EditorGui.DragFloat("Vignette Intensity", ref intensity, 0.01f, 0.0f, 5.0f)) pp.VignetteIntensity = intensity;
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
        ImGui.TextUnformatted("Texture");
        ImGui.Button(material.Texture != null
            ? $"{System.IO.Path.GetFileName(material.TexturePath) ?? "Texture"} (Drop to change)"
            : "Drop Texture Here", new Vector2(-1, 30));
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("IMAGE_FILE");
                if (payload.NativePtr != null)
                {
                    string? filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                    if (filepath != null)
                    {
                        try
                        {
                            material.SetTexture(filepath);
                            material.Save(path);
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine($"Failed to set material texture: {ex.Message}");
                        }
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (material.Texture != null)
        {
            if (ImGui.Button("Remove Texture", new Vector2(-1, 24)))
            {
                material.SetTexture(null);
                material.Save(path);
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Normal Map");
        ImGui.Button(material.NormalMap != null
            ? $"{System.IO.Path.GetFileName(material.NormalMapPath) ?? "Normal Map"} (Drop to change)"
            : "Drop Normal Map Here", new Vector2(-1, 30));
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("IMAGE_FILE");
                if (payload.NativePtr != null)
                {
                    string? filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                    if (filepath != null)
                    {
                        try
                        {
                            material.SetNormalMap(filepath);
                            material.Save(path);
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine($"Failed to set material normal map: {ex.Message}");
                        }
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (material.NormalMap != null)
        {
            if (ImGui.Button("Remove Normal Map", new Vector2(-1, 24)))
            {
                material.SetNormalMap(null);
                material.Save(path);
            }
        }
    }

    private static void AssignMaterial(MeshRenderer meshRenderer, string path)
    {
        try
        {
            meshRenderer.Material = Material.Load(path);
            meshRenderer.MaterialPath = path;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Failed to load material '{path}': {ex.Message}");
        }
    }

    private static System.Collections.Generic.List<string> EnumerateProjectMaterials()
    {
        var result = new System.Collections.Generic.List<string>();
        string? dir = Spot.Core.Project.Active?.GetAssetDirectory();
        if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
        {
            try
            {
                result.AddRange(System.IO.Directory.EnumerateFiles(dir, "*.sptmat", System.IO.SearchOption.AllDirectories));
            }
            catch
            {
            }
        }
        return result;
    }
}
