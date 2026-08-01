using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.Scenes;
using Spot.Rendering;
using Spot.Editor.UI;

namespace Spot.Editor.Panels;

public class HierarchyPanel
{
    public Action<Entity>? OnEntityDoubleClicked;
    private readonly EditorContext _context;

    public HierarchyPanel(EditorContext context)
    {
        _context = context;
    }

    public void OnImGuiRender(ref bool open)
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse;
        ImGui.Begin("Hierarchy", ref open, flags);

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
                    CreateEmpty();
                }
                if (ImGui.MenuItem("Create Camera"))
                {
                    CreateCamera();
                }
                if (ImGui.MenuItem("Create Sprite"))
                {
                    CreateSprite();
                }
                if (ImGui.BeginMenu("Light"))
                {
                    if (ImGui.MenuItem("Directional Light")) CreateLight(LightType.Directional);
                    if (ImGui.MenuItem("Point Light")) CreateLight(LightType.Point);
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("3D Object"))
                {
                    if (ImGui.MenuItem("Cube")) CreatePrimitive("Cube");
                    if (ImGui.MenuItem("Plane")) CreatePrimitive("Plane");
                    if (ImGui.MenuItem("Quad")) CreatePrimitive("Quad");
                    if (ImGui.MenuItem("Sphere")) CreatePrimitive("Sphere");
                    ImGui.EndMenu();
                }
                ImGui.EndPopup();
            }
        }

        ImGui.End();
    }

    /// <summary>Creates an empty entity, selects it, and returns it.</summary>
    public Entity CreateEmpty() => CreateEntity("Empty Entity");

    /// <summary>Creates an entity with a <see cref="CameraComponent"/>, selects it, and returns it.</summary>
    public Entity CreateCamera()
    {
        var entity = CreateEntity("Camera");
        entity.AddComponent(new CameraComponent());
        return entity;
    }

    /// <summary>Creates an entity with a <see cref="Sprite2DComponent"/> component, selects it, and returns it.</summary>
    public Entity CreateSprite()
    {
        var entity = CreateEntity("Sprite");
        entity.AddComponent(new Sprite2DComponent());
        return entity;
    }

    /// <summary>Creates an entity with an empty <see cref="MeshComponent"/> component, selects it, and returns it.</summary>
    public Entity CreateMesh()
    {
        var entity = CreateEntity("Mesh");
        entity.AddComponent(new MeshComponent());
        return entity;
    }

    /// <summary>Creates an entity with a <see cref="LightComponent"/>, selects it, and returns it.</summary>
    public Entity CreateLight(LightType type)
    {
        var entity = CreateEntity(type == LightType.Directional ? "Directional Light" : "Point Light");
        var transform = entity.GetComponent<TransformComponent>();
        if (type == LightType.Directional)
        {
            transform.Rotation = new System.Numerics.Vector3(-45.0f, 45.0f, 0.0f);
        }
        var light = new LightComponent { Type = type };
        entity.AddComponent(light);
        return entity;
    }

    /// <summary>Creates an entity with a procedural primitive <see cref="MeshComponent"/>, selects it, and returns it.</summary>
    public Entity CreatePrimitive(string typeName)
    {
        var entity = CreateEntity(typeName);
        var meshRenderer = new MeshComponent { ModelPath = $"primitive:{typeName}" };
        try
        {
            meshRenderer.Model = Spot.Assets.Model.Load(meshRenderer.ModelPath);
        }
        catch (System.Exception ex)
        {
            Spot.Core.Log.Error("Failed to load primitive '{0}': {1}", typeName, ex.Message);
        }
        entity.AddComponent(meshRenderer);
        return entity;
    }

    /// <summary>
    /// Creates an entity with a <see cref="MeshComponent"/> loaded from the given model file, selects it,
    /// and returns it. The model is loaded eagerly; failures are logged and leave the renderer empty.
    /// </summary>
    public Entity CreateMeshFromModel(string modelPath)
    {
        var entity = CreateEntity(System.IO.Path.GetFileNameWithoutExtension(modelPath));
        var meshRenderer = new MeshComponent { ModelPath = modelPath };
        try
        {
            meshRenderer.Model = Spot.Assets.Model.Load(modelPath);
        }
        catch (System.Exception ex)
        {
            Spot.Core.Log.Error("Failed to load model '{0}': {1}", modelPath, ex.Message);
        }
        entity.AddComponent(meshRenderer);
        return entity;
    }

    // Creates a new root entity, selects it, and returns it so callers can attach extra components.
    private Entity CreateEntity(string name)
    {
        var entity = _context.ActiveScene!.Instantiate(name);
        _context.Selection = entity;
        return entity;
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
        
        // Reserve room at the start of the label for a type icon, drawn afterwards over that space.
        var iconKind = EditorGui.IconFor(entity);
        float iconSize = ImGui.GetTextLineHeight();
        string label = EditorGui.IconPadding(iconSize + 4.0f) + name;

        bool active = entity.IsActiveInHierarchy();
        if (!active) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));

        bool opened = ImGui.TreeNodeEx((IntPtr)entity.GetHashCode(), flags, label);

        if (!active) ImGui.PopStyleColor();

        // Draw the type icon just after the tree arrow, vertically centered on the row.
        var drawList = ImGui.GetWindowDrawList();
        Vector2 rectMin = ImGui.GetItemRectMin();
        float rowHeight = ImGui.GetItemRectSize().Y;
        float iconLeft = rectMin.X + ImGui.GetTreeNodeToLabelSpacing();
        Vector2 iconCenter = new(iconLeft + iconSize * 0.5f, rectMin.Y + rowHeight * 0.5f);
        EditorGui.DrawEntityIcon(drawList, iconKind, iconCenter, iconSize * 0.42f, active ? 1.0f : 0.5f);

        if (ImGui.IsItemClicked())
        {
            _context.Selection = entity;
        }
        
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            OnEntityDoubleClicked?.Invoke(entity);
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
