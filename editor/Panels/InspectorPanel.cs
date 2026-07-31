using System;
using ImGuiNET;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Physics;
using Spot.Assets;

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
            DrawComponents(_context.Selection.Value);
            
            ImGui.Separator();
            if (ImGui.Button("Add Component"))
            {
                ImGui.OpenPopup("AddComponent");
            }

            if (ImGui.BeginPopup("AddComponent"))
            {
                if (ImGui.MenuItem("Sprite2D"))
                {
                    if (!_context.Selection.Value.HasComponent<Sprite2D>())
                        _context.Selection.Value.AddComponent(new Sprite2D());
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("Mesh Renderer"))
                {
                    if (!_context.Selection.Value.HasComponent<MeshRenderer>())
                        _context.Selection.Value.AddComponent(new MeshRenderer());
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("Camera"))
                {
                    if (!_context.Selection.Value.HasComponent<CameraComponent>())
                        _context.Selection.Value.AddComponent(new CameraComponent());
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("PhysicsBody2D"))
                {
                    if (!_context.Selection.Value.HasComponent<PhysicsBody2DComponent>())
                        _context.Selection.Value.AddComponent(new PhysicsBody2DComponent());
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("BoxCollider2D"))
                {
                    if (!_context.Selection.Value.HasComponent<BoxCollider2DComponent>())
                        _context.Selection.Value.AddComponent(new BoxCollider2DComponent());
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("Directional Light"))
                {
                    if (!_context.Selection.Value.HasComponent<DirectionalLightComponent>())
                        _context.Selection.Value.AddComponent(new DirectionalLightComponent());
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("Dynamic Clouds"))
                {
                    if (!_context.Selection.Value.HasComponent<DynamicCloudsComponent>())
                        _context.Selection.Value.AddComponent(new DynamicCloudsComponent());
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("Script Component"))
                {
                    if (!_context.Selection.Value.HasComponent<ScriptComponent>())
                        _context.Selection.Value.AddComponent(new ScriptComponent());
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        ImGui.End();
    }

    private void DrawMaterialEditor(string path)
    {
        // Material.Load caches by path and never throws (it logs and returns a default on failure),
        // so editing this instance updates every model referencing the same file live.
        var material = Material.Load(path);

        ImGui.TextDisabled(System.IO.Path.GetFileName(path));
        ImGui.Separator();

        var color = material.Color;
        if (ImGui.ColorEdit4("Color", ref color))
            material.Color = color;
        if (ImGui.IsItemDeactivatedAfterEdit())
            material.Save(path);

        string[] shaderTypes = Enum.GetNames(typeof(Spot.Assets.MaterialShaderType));
        int currentShader = (int)material.ShaderType;
        if (ImGui.Combo("Shader Type", ref currentShader, shaderTypes, shaderTypes.Length))
        {
            material.ShaderType = (Spot.Assets.MaterialShaderType)currentShader;
            material.Save(path);
        }

        if (material.ShaderType == Spot.Assets.MaterialShaderType.Water)
        {
            bool waterChanged = false;
            float waveSpeed = material.WaveSpeed;
            if (ImGui.DragFloat("Wave Speed", ref waveSpeed, 0.01f, 0.0f, 10.0f))
            {
                material.WaveSpeed = waveSpeed;
                waterChanged = true;
            }
            float waveScale = material.WaveScale;
            if (ImGui.DragFloat("Wave Scale", ref waveScale, 0.01f, 0.0f, 10.0f))
            {
                material.WaveScale = waveScale;
                waterChanged = true;
            }
            float waveStrength = material.WaveStrength;
            if (ImGui.DragFloat("Wave Strength", ref waveStrength, 0.01f, 0.0f, 5.0f))
            {
                material.WaveStrength = waveStrength;
                waterChanged = true;
            }
            float specPower = material.SpecularPower;
            if (ImGui.DragFloat("Specular Power", ref specPower, 1.0f, 1.0f, 512.0f))
            {
                material.SpecularPower = specPower;
                waterChanged = true;
            }
            
            if (waterChanged) material.Save(path);
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Texture");
        ImGui.Button(material.Texture != null
            ? $"{System.IO.Path.GetFileName(material.TexturePath) ?? "Texture"} (Drop to change)"
            : "Drop Texture Here", new System.Numerics.Vector2(-1, 30));
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("IMAGE_FILE");
                if (payload.NativePtr != null)
                {
                    string filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
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
            if (ImGui.Button("Remove Texture", new System.Numerics.Vector2(-1, 24)))
            {
                material.SetTexture(null);
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
        string? dir = Spot.Build.Project.Active?.GetAssetDirectory();
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

    private void DrawComponents(Entity entity)
    {
        if (entity.HasComponent<TagComponent>())
        {
            var tag = entity.GetComponent<TagComponent>();
            bool active = tag.Enabled;
            if (ImGui.Checkbox("##Active", ref active))
                tag.Enabled = active;
            ImGui.SameLine();
            string name = tag.Name;
            if (ImGui.InputText("Tag", ref name, 256))
            {
                tag.Name = name;
            }
        }

        if (entity.HasComponent<Transform>())
        {
            if (ImGui.TreeNodeEx((IntPtr)typeof(Transform).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen, "Transform"))
            {
                var transform = entity.GetComponent<Transform>();
                
                bool enabled = transform.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                    transform.Enabled = enabled;
                var position = transform.Position;
                if (ImGui.DragFloat3("Position", ref position, 0.1f))
                    transform.Position = position;
                
                var rotation = transform.Rotation;
                if (ImGui.DragFloat3("Rotation", ref rotation, 0.1f))
                    transform.Rotation = rotation;
                
                var scale = transform.Scale;
                if (ImGui.DragFloat3("Scale", ref scale, 0.1f))
                    transform.Scale = scale;
                    
                ImGui.TreePop();
            }
        }

        if (entity.HasComponent<Sprite2D>())
        {
            ImGui.PushID("Sprite2D");
            bool opened = ImGui.TreeNodeEx((IntPtr)typeof(Sprite2D).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap, "Sprite2D");
            ImGui.SameLine(ImGui.GetWindowWidth() - 30.0f);
            if (ImGui.Button("..."))
            {
                ImGui.OpenPopup("ComponentSettings");
            }
            
            bool removeComponent = false;
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeComponent = true;
                ImGui.EndPopup();
            }

            if (opened)
            {
                var sprite = entity.GetComponent<Sprite2D>();
                
                bool enabled = sprite.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                    sprite.Enabled = enabled;
                var color = sprite.Color;
                if (ImGui.ColorEdit4("Color", ref color))
                    sprite.Color = color;
                    
                ImGui.Button(sprite.Texture != null ? "Texture Loaded (Drop to change)" : "Drop Texture Here", new System.Numerics.Vector2(-1, 30));
                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        var payload = ImGui.AcceptDragDropPayload("IMAGE_FILE");
                        if (payload.NativePtr != null)
                        {
                            string filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                            if (filepath != null)
                            {
                                try
                                {
                                    var newTexture = new Texture2D(filepath);
                                    if (sprite.Texture != null)
                                    {
                                        sprite.Texture.Dispose();
                                    }
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
                    if (ImGui.Button("Remove Texture", new System.Numerics.Vector2(-1, 24)))
                    {
                        sprite.Texture.Dispose();
                        sprite.Texture = null;
                        sprite.TexturePath = null;
                    }
                }
                    
                ImGui.TreePop();
            }
            
            if (removeComponent)
                entity.RemoveComponent<Sprite2D>();

            ImGui.PopID();
        }

        if (entity.HasComponent<MeshRenderer>())
        {
            ImGui.PushID("MeshRenderer");
            bool opened = ImGui.TreeNodeEx((IntPtr)typeof(MeshRenderer).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap, "Mesh Renderer");
            ImGui.SameLine(ImGui.GetWindowWidth() - 30.0f);
            if (ImGui.Button("..."))
            {
                ImGui.OpenPopup("ComponentSettings");
            }

            bool removeComponent = false;
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeComponent = true;
                ImGui.EndPopup();
            }

            if (opened)
            {
                var meshRenderer = entity.GetComponent<MeshRenderer>();

                bool enabled = meshRenderer.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                    meshRenderer.Enabled = enabled;
                var color = meshRenderer.Color;
                if (ImGui.ColorEdit4("Color", ref color))
                    meshRenderer.Color = color;

                string modelLabel = meshRenderer.Model != null
                    ? $"{System.IO.Path.GetFileName(meshRenderer.ModelPath) ?? "Model"} (Drop to change)"
                    : "Drop 3D Model Here";
                bool modelClicked = ImGui.Button(modelLabel, new System.Numerics.Vector2(-1, 30));
                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        var payload = ImGui.AcceptDragDropPayload("MODEL_FILE");
                        if (payload.NativePtr != null)
                        {
                            string filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
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
                bool materialClicked = ImGui.Button(materialLabel, new System.Numerics.Vector2(-1, 30));

                // Also accept a material dragged from the Asset Browser.
                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        var payload = ImGui.AcceptDragDropPayload("MATERIAL_FILE");
                        if (payload.NativePtr != null)
                        {
                            string filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
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

                ImGui.TreePop();
            }

            if (removeComponent)
                entity.RemoveComponent<MeshRenderer>();

            ImGui.PopID();
        }

        if (entity.HasComponent<CameraComponent>())
        {
            ImGui.PushID("CameraComponent");
            bool opened = ImGui.TreeNodeEx((IntPtr)typeof(CameraComponent).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap, "Camera");
            ImGui.SameLine(ImGui.GetWindowWidth() - 30.0f);
            if (ImGui.Button("..."))
            {
                ImGui.OpenPopup("ComponentSettings");
            }
            
            bool removeComponent = false;
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeComponent = true;
                ImGui.EndPopup();
            }

            if (opened)
            {
                var cameraComp = entity.GetComponent<CameraComponent>();
                
                bool enabled = cameraComp.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                    cameraComp.Enabled = enabled;
                bool primary = cameraComp.Primary;
                if (ImGui.Checkbox("Primary", ref primary))
                    cameraComp.Primary = primary;
                    
                string[] projectionTypes = { "Orthographic", "Perspective" };
                int currentProjection = (int)cameraComp.ProjectionType;
                if (ImGui.Combo("Projection", ref currentProjection, projectionTypes, projectionTypes.Length))
                {
                    cameraComp.ProjectionType = (SceneCameraProjection)currentProjection;
                }
                    
                if (cameraComp.ProjectionType == SceneCameraProjection.Perspective)
                {
                    float fov = cameraComp.FieldOfView;
                    if (ImGui.DragFloat("Field Of View", ref fov, 1.0f, 1.0f, 180.0f))
                        cameraComp.FieldOfView = fov;
                }
                else
                {
                    float zoom = cameraComp.ZoomLevel;
                    if (ImGui.DragFloat("Zoom Level", ref zoom, 0.1f, 0.1f, 100.0f))
                        cameraComp.ZoomLevel = zoom;
                }
                    
                bool fixedAspect = cameraComp.FixedAspectRatio;
                if (ImGui.Checkbox("Fixed Aspect Ratio", ref fixedAspect))
                    cameraComp.FixedAspectRatio = fixedAspect;

                var bgColor = cameraComp.BackgroundColor;
                if (ImGui.ColorEdit4("Background", ref bgColor))
                    cameraComp.BackgroundColor = bgColor;

                ImGui.TreePop();
            }
            
            if (removeComponent)
                entity.RemoveComponent<CameraComponent>();
                
            ImGui.PopID();
        }

        if (entity.HasComponent<PhysicsBody2DComponent>())
        {
            ImGui.PushID("PhysicsBody2DComponent");
            bool opened = ImGui.TreeNodeEx((IntPtr)typeof(PhysicsBody2DComponent).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap, "Physics Body 2D");
            ImGui.SameLine(ImGui.GetWindowWidth() - 30.0f);
            if (ImGui.Button("..."))
            {
                ImGui.OpenPopup("ComponentSettings");
            }
            
            bool removeComponent = false;
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeComponent = true;
                ImGui.EndPopup();
            }

            if (opened)
            {
                var body = entity.GetComponent<PhysicsBody2DComponent>();
                
                bool enabled = body.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                    body.Enabled = enabled;
                var velocity = body.Velocity;
                if (ImGui.DragFloat2("Velocity", ref velocity, 0.1f))
                    body.Velocity = velocity;
                    
                float gravity = body.GravityScale;
                if (ImGui.DragFloat("Gravity Scale", ref gravity, 0.1f))
                    body.GravityScale = gravity;
                    
                bool isDynamic = body.IsDynamic;
                if (ImGui.Checkbox("Is Dynamic", ref isDynamic))
                    body.IsDynamic = isDynamic;

                ImGui.TreePop();
            }
            
            if (removeComponent)
                entity.RemoveComponent<PhysicsBody2DComponent>();
                
            ImGui.PopID();
        }

        if (entity.HasComponent<BoxCollider2DComponent>())
        {
            ImGui.PushID("BoxCollider2DComponent");
            bool opened = ImGui.TreeNodeEx((IntPtr)typeof(BoxCollider2DComponent).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap, "Box Collider 2D");
            ImGui.SameLine(ImGui.GetWindowWidth() - 30.0f);
            if (ImGui.Button("..."))
            {
                ImGui.OpenPopup("ComponentSettings");
            }
            
            bool removeComponent = false;
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeComponent = true;
                ImGui.EndPopup();
            }

            if (opened)
            {
                var collider = entity.GetComponent<BoxCollider2DComponent>();
                
                bool enabled = collider.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                    collider.Enabled = enabled;
                var size = collider.Size;
                if (ImGui.DragFloat2("Size", ref size, 0.1f))
                    collider.Size = size;
                    
                var offset = collider.Offset;
                if (ImGui.DragFloat2("Offset", ref offset, 0.1f))
                    collider.Offset = offset;

                ImGui.TreePop();
            }
            
            if (removeComponent)
                entity.RemoveComponent<BoxCollider2DComponent>();
                
            ImGui.PopID();
        }

        if (entity.HasComponent<DirectionalLightComponent>())
        {
            ImGui.PushID("DirectionalLightComponent");
            bool opened = ImGui.TreeNodeEx((IntPtr)typeof(DirectionalLightComponent).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap, "Directional Light");
            ImGui.SameLine(ImGui.GetWindowWidth() - 30.0f);
            if (ImGui.Button("..."))
            {
                ImGui.OpenPopup("ComponentSettings");
            }
            
            bool removeComponent = false;
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeComponent = true;
                ImGui.EndPopup();
            }

            if (opened)
            {
                var light = entity.GetComponent<DirectionalLightComponent>();
                
                bool enabled = light.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                    light.Enabled = enabled;
                var color = light.Color;
                if (ImGui.ColorEdit3("Color", ref color))
                    light.Color = color;
                    
                float intensity = light.Intensity;
                if (ImGui.DragFloat("Intensity", ref intensity, 0.05f, 0.0f, 10.0f))
                    light.Intensity = intensity;
                    
                float ambientIntensity = light.AmbientIntensity;
                if (ImGui.DragFloat("Ambient Intensity", ref ambientIntensity, 0.01f, 0.0f, 1.0f))
                    light.AmbientIntensity = ambientIntensity;

                ImGui.TreePop();
            }
            
            if (removeComponent)
                entity.RemoveComponent<DirectionalLightComponent>();
                
            ImGui.PopID();
        }

        if (entity.HasComponent<DynamicCloudsComponent>())
        {
            ImGui.PushID("DynamicCloudsComponent");
            bool opened = ImGui.TreeNodeEx((IntPtr)typeof(DynamicCloudsComponent).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap, "Dynamic Clouds");
            ImGui.SameLine(ImGui.GetWindowWidth() - 30.0f);
            if (ImGui.Button("..."))
            {
                ImGui.OpenPopup("ComponentSettings");
            }
            
            bool removeComponent = false;
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeComponent = true;
                ImGui.EndPopup();
            }

            if (opened)
            {
                var clouds = entity.GetComponent<DynamicCloudsComponent>();
                
                bool enabled = clouds.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                    clouds.Enabled = enabled;
                
                var topColor = clouds.ColorTop;
                if (ImGui.ColorEdit3("Color Top", ref topColor))
                    clouds.ColorTop = topColor;

                var botColor = clouds.ColorBottom;
                if (ImGui.ColorEdit3("Color Bottom", ref botColor))
                    clouds.ColorBottom = botColor;
                    
                float speed = clouds.Speed;
                if (ImGui.DragFloat("Speed", ref speed, 0.01f, 0.0f, 10.0f))
                    clouds.Speed = speed;

                float density = clouds.Density;
                if (ImGui.DragFloat("Density", ref density, 0.01f, 0.0f, 1.0f))
                    clouds.Density = density;

                float height = clouds.Height;
                if (ImGui.DragFloat("Height", ref height, 0.01f, 0.0f, 10.0f))
                    clouds.Height = height;

                float opacity = clouds.Opacity;
                if (ImGui.DragFloat("Opacity", ref opacity, 0.01f, 0.0f, 1.0f))
                    clouds.Opacity = opacity;
                    
                float volume = clouds.Volume;
                if (ImGui.DragFloat("Volume", ref volume, 0.01f, 0.0f, 5.0f))
                    clouds.Volume = volume;

                ImGui.TreePop();
            }
            
            if (removeComponent)
                entity.RemoveComponent<DynamicCloudsComponent>();
                
            ImGui.PopID();
        }

        if (entity.HasComponent<ScriptComponent>())
        {
            ImGui.PushID("ScriptComponent");
            bool opened = ImGui.TreeNodeEx((IntPtr)typeof(ScriptComponent).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowOverlap, "Script Component");
            ImGui.SameLine(ImGui.GetWindowWidth() - 30.0f);
            if (ImGui.Button("..."))
            {
                ImGui.OpenPopup("ComponentSettings");
            }
            
            bool removeComponent = false;
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeComponent = true;
                ImGui.EndPopup();
            }

            if (opened)
            {
                var scriptComp = entity.GetComponent<ScriptComponent>();
                
                bool enabled = scriptComp.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                    scriptComp.Enabled = enabled;
                int scriptToRemove = -1;
                for (int i = 0; i < scriptComp.ClassNames.Count; i++)
                {
                    string className = scriptComp.ClassNames[i];
                    ImGui.SetNextItemWidth(ImGui.GetWindowWidth() - 50.0f);
                    if (ImGui.InputText($"##Script{i}", ref className, 256))
                    {
                        scriptComp.ClassNames[i] = className;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"X##{i}"))
                    {
                        scriptToRemove = i;
                    }
                }
                
                if (scriptToRemove >= 0)
                {
                    scriptComp.ClassNames.RemoveAt(scriptToRemove);
                }
                
                ImGui.Button("Drop Script Here", new System.Numerics.Vector2(-1, 30));
                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        var payload = ImGui.AcceptDragDropPayload("SCRIPT_FILE");
                        if (payload.NativePtr != null)
                        {
                            string filename = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                            if (filename != null && filename.EndsWith(".cs"))
                            {
                                string cName = filename.Substring(0, filename.Length - 3);
                                if (!scriptComp.ClassNames.Contains(cName))
                                {
                                    scriptComp.ClassNames.Add(cName);
                                }
                            }
                        }
                    }
                    ImGui.EndDragDropTarget();
                }

                ImGui.TreePop();
            }
            
            if (removeComponent)
                entity.RemoveComponent<ScriptComponent>();
                
            ImGui.PopID();
        }
    }
}
