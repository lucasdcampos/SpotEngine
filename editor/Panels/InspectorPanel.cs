using System;
using ImGuiNET;
using Spot.Rendering;
using Spot.Scenes;

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
    }
}
