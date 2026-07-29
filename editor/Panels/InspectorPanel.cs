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
        }

        ImGui.End();
    }

    private void DrawComponents(Entity entity)
    {
        if (entity.HasComponent<TagComponent>())
        {
            var tag = entity.GetComponent<TagComponent>();
            ImGui.Text($"Tag: {tag.Name}");
        }

        if (entity.HasComponent<Transform>())
        {
            if (ImGui.TreeNodeEx((IntPtr)typeof(Transform).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen, "Transform"))
            {
                var transform = entity.GetComponent<Transform>();
                ImGui.Text($"Position: {transform.Position.X:0.00}, {transform.Position.Y:0.00}, {transform.Position.Z:0.00}");
                ImGui.Text($"Rotation: {transform.Rotation.X:0.00}, {transform.Rotation.Y:0.00}, {transform.Rotation.Z:0.00}");
                ImGui.Text($"Scale: {transform.Scale.X:0.00}, {transform.Scale.Y:0.00}, {transform.Scale.Z:0.00}");
                ImGui.TreePop();
            }
        }

        if (entity.HasComponent<Sprite2D>())
        {
            if (ImGui.TreeNodeEx((IntPtr)typeof(Sprite2D).GetHashCode(), ImGuiTreeNodeFlags.DefaultOpen, "Sprite2D"))
            {
                var sprite = entity.GetComponent<Sprite2D>();
                ImGui.Text($"Color: {sprite.Color.X:0.00}, {sprite.Color.Y:0.00}, {sprite.Color.Z:0.00}, {sprite.Color.W:0.00}");
                ImGui.TreePop();
            }
        }
    }
}
