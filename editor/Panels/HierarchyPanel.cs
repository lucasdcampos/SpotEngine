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

            if (ImGui.BeginPopupContextWindow("HierarchyContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
            {
                if (ImGui.MenuItem("Create Empty Entity"))
                {
                    var entity = _context.ActiveScene.Instantiate("Empty Entity");
                    _context.Selection = entity;
                }
                ImGui.EndPopup();
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

        bool entityDeleted = false;
        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Delete Entity"))
            {
                entityDeleted = true;
            }
            ImGui.EndPopup();
        }

        if (opened)
        {
            ImGui.TreePop();
        }

        if (entityDeleted)
        {
            _context.ActiveScene?.Destroy(entity);
            if (_context.Selection != null && _context.Selection.Value == entity)
                _context.Selection = null;
        }
    }
}
