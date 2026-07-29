using System;
using ImGuiNET;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Physics;

namespace Spot.Editor.Panels;

public class InspectorPanel
{
    private readonly EditorContext _context;

    public InspectorPanel(EditorContext context)
    {
        _context = context;
    }

    public void OnImGuiRender()
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        ImGui.Begin("Inspector", flags);

        if (_context.Selection != null)
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
                ImGui.EndPopup();
            }
        }

        ImGui.End();
    }

    private void DrawComponents(Entity entity)
    {
        if (entity.HasComponent<TagComponent>())
        {
            var tag = entity.GetComponent<TagComponent>();
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
                
                var color = sprite.Color;
                if (ImGui.ColorEdit4("Color", ref color))
                    sprite.Color = color;
                    
                ImGui.TreePop();
            }
            
            if (removeComponent)
                entity.RemoveComponent<Sprite2D>();
                
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
                
                bool primary = cameraComp.Primary;
                if (ImGui.Checkbox("Primary", ref primary))
                    cameraComp.Primary = primary;
                    
                bool fixedAspect = cameraComp.FixedAspectRatio;
                if (ImGui.Checkbox("Fixed Aspect Ratio", ref fixedAspect))
                    cameraComp.FixedAspectRatio = fixedAspect;
                    
                float zoom = cameraComp.ZoomLevel;
                if (ImGui.DragFloat("Zoom Level", ref zoom, 0.1f, 0.1f, 100.0f))
                    cameraComp.ZoomLevel = zoom;

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
    }
}
