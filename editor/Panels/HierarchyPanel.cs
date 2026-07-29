using System;
using System.Linq;
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
                if (entity.Parent == null)
                {
                    DrawEntityNode(entity);
                }
            }

            if (ImGui.IsMouseDown(0) && ImGui.IsWindowHovered())
            {
                _context.Selection = null;
            }

            // Allow dropping on the empty space to clear parent
            if (ImGui.BeginDragDropTarget())
            {
                unsafe
                {
                    var payload = ImGui.AcceptDragDropPayload("ENTITY");
                    if (payload.NativePtr != null)
                    {
                        int payloadId = *(int*)payload.Data;
                        Entity draggedEntity = new Entity(payloadId, _context.ActiveScene);
                        draggedEntity.SetParent(null);
                    }
                }
                ImGui.EndDragDropTarget();
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
        
        bool hasChildren = entity.Children.Any();
        if (!hasChildren)
        {
            flags |= ImGuiTreeNodeFlags.Leaf;
        }
        
        bool opened = ImGui.TreeNodeEx((IntPtr)entity.GetHashCode(), flags, name);
        if (ImGui.IsItemClicked())
        {
            _context.Selection = entity;
        }

        // Drag Source
        if (ImGui.BeginDragDropSource())
        {
            unsafe
            {
                int id = entity.Id;
                ImGui.SetDragDropPayload("ENTITY", (IntPtr)(&id), 4);
            }
            ImGui.Text(entity.Name);
            ImGui.EndDragDropSource();
        }

        // Drag Target
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("ENTITY");
                if (payload.NativePtr != null)
                {
                    int payloadId = *(int*)payload.Data;
                    Entity draggedEntity = new Entity(payloadId, entity.Scene);
                    // Prevent reparenting to self or a child (basic check, could be recursive)
                    if (draggedEntity != entity)
                    {
                        draggedEntity.SetParent(entity);
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        bool entityDeleted = false;
        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Delete Entity"))
            {
                entityDeleted = true;
            }
            if (ImGui.MenuItem("Create Child Entity"))
            {
                var child = _context.ActiveScene!.Instantiate("Empty Entity");
                child.SetParent(entity);
                _context.Selection = child;
            }
            ImGui.EndPopup();
        }

        if (opened)
        {
            if (hasChildren)
            {
                foreach (var child in entity.Children.ToList()) // ToList to avoid modification during iteration
                {
                    DrawEntityNode(child);
                }
            }
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
