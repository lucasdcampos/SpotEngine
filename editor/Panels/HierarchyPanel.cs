using System;
using ImGuiNET;
using Spot.Scenes;

namespace Spot.Editor.Panels;

public class HierarchyPanel
{
    private readonly EditorContext _context;

    public HierarchyPanel(EditorContext context)
    {
        _context = context;
    }

    public void OnImGuiRender()
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        ImGui.Begin("Hierarchy", flags);

        if (_context.ActiveScene != null)
        {
            var view = _context.ActiveScene.View<TagComponent>();
            foreach (var entity in view)
            {
                DrawEntityNode(entity);
            }

            if (ImGui.IsMouseDown(0) && ImGui.IsWindowHovered())
            {
                _context.Selection = null;
            }
        }

        ImGui.End();
    }

    private void DrawEntityNode(Entity entity)
    {
        string name = entity.Name;
        ImGuiTreeNodeFlags flags = ((_context.Selection != null && _context.Selection.Value == entity) ? ImGuiTreeNodeFlags.Selected : 0) | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        
        bool opened = ImGui.TreeNodeEx((IntPtr)entity.GetHashCode(), flags, name);
        if (ImGui.IsItemClicked())
        {
            _context.Selection = entity;
        }

        if (opened)
        {
            ImGui.TreePop();
        }
    }
}
