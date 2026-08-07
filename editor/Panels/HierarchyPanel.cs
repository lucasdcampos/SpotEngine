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

    private int _renamingEntityId = -1;
    private string _entityRenameBuffer = "";
    private bool _renameFocusPending;
    private bool _isNewEntity;

    // Explicit root-entity ordering (dictionary pools have no stable order for reordering).
    private readonly List<int> _rootOrder = new();

    // Tint for prefab-instance entities; matches the asset browser's prefab color.
    private static readonly Vector4 PrefabColor = new(0.40f, 0.82f, 0.92f, 1.0f);

    public HierarchyPanel(EditorContext context)
    {
        _context = context;
    }

    public void OnImGuiRender(ref bool open)
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse;
        ImGui.Begin("Scene", ref open, flags);

        // Deeper child indentation and a little extra row spacing make the nesting readable at a glance
        // without changing any behavior.
        var style = ImGui.GetStyle();
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 24.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(style.ItemSpacing.X, 5.0f));

        if (_context.ActiveScene != null)
        {
            SyncRootOrder();
            foreach (int rootId in _rootOrder.ToList())
            {
                DrawEntityNode(new Entity(rootId, _context.ActiveScene));
            }

            if (ImGui.IsMouseDown(0) && ImGui.IsWindowHovered())
            {
                _context.Selection = null;
            }

            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && _renamingEntityId == -1 && _context.Selection != null)
            {
                var sel = _context.Selection.Value;
                if (ImGui.IsKeyPressed(ImGuiKey.F2))
                {
                    _renamingEntityId = sel.Id;
                    _entityRenameBuffer = sel.Name;
                    _renameFocusPending = true;
                    _isNewEntity = false;
                }
                else if (ImGui.IsKeyPressed(ImGuiKey.Delete))
                {
                    _context.ActiveScene?.Destroy(sel);
                    _context.Selection = null;
                }
            }

            // Allow dropping on the empty space to clear parent, or to instantiate a prefab at the root.
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

                    var prefabPayload = ImGui.AcceptDragDropPayload("PREFAB_FILE");
                    if (prefabPayload.NativePtr != null)
                    {
                        string? path = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(prefabPayload.Data);
                        if (path != null) InstantiatePrefab(path, null);
                    }
                }
                ImGui.EndDragDropTarget();
            }

            if (ImGui.BeginPopupContextWindow("SceneContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
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

        ImGui.PopStyleVar(2);
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

    // Instantiates a prefab file into the active scene under an optional parent, marks the new root as an
    // instance of that prefab, and selects it. Failures are logged by the loader and leave the scene unchanged.
    private void InstantiatePrefab(string path, Entity? parent)
    {
        var scene = _context.ActiveScene;
        if (scene == null) return;

        Entity? root = Prefab.InstantiateFile(scene, path, parent);
        if (root == null) return;

        string? reference = Spot.Assets.AssetDatabase.ToGuidRef(path);
        root.Value.AddComponent(new PrefabComponent { PrefabRef = reference });
        _context.Selection = root.Value;
    }

    // Creates a new root entity, selects it, triggers inline rename, and returns it.
    private Entity CreateEntity(string name)
    {
        var entity = _context.ActiveScene!.Instantiate(name);
        _context.Selection = entity;
        _renamingEntityId = entity.Id;
        _entityRenameBuffer = name;
        _renameFocusPending = true;
        _isNewEntity = true;
        return entity;
    }

    private void DrawEntityNode(Entity entity)
    {
        string name = entity.Name;
        bool isRenaming = _renamingEntityId == entity.Id;

        ImGuiTreeNodeFlags flags = ((_context.Selection != null && _context.Selection.Value == entity) ? ImGuiTreeNodeFlags.Selected : 0) | ImGuiTreeNodeFlags.OpenOnArrow;
        if (!isRenaming) flags |= ImGuiTreeNodeFlags.SpanAvailWidth;

        bool hasChildren = entity.Children.Any();
        if (!hasChildren)
        {
            flags |= ImGuiTreeNodeFlags.Leaf;
        }

        // When renaming, the tree node only shows the glyph; the InputText takes the name's place on the same line.
        string glyphPrefix = EditorGui.EntityGlyph(entity) + "   ";
        string label = isRenaming ? glyphPrefix : glyphPrefix + name;

        // Prefab instances read in a distinct color; a disabled entity is dimmed and takes precedence.
        bool active = entity.IsActiveInHierarchy();
        bool isPrefab = entity.HasComponent<PrefabComponent>();
        Vector4? textColor = !active ? new Vector4(0.5f, 0.5f, 0.5f, 1.0f)
                           : isPrefab ? PrefabColor
                           : null;
        if (textColor != null) ImGui.PushStyleColor(ImGuiCol.Text, textColor.Value);

        bool opened = ImGui.TreeNodeEx((IntPtr)entity.GetHashCode(), flags, label);

        if (textColor != null) ImGui.PopStyleColor();

        if (isRenaming)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 4);
            bool focusThisFrame = _renameFocusPending;
            if (_renameFocusPending)
            {
                ImGui.SetKeyboardFocusHere();
                _renameFocusPending = false;
            }
            bool submitted = ImGui.InputText("##entityrename", ref _entityRenameBuffer, 128,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            bool escaped = ImGui.IsKeyPressed(ImGuiKey.Escape);
            bool lostFocus = !focusThisFrame && !ImGui.IsItemActive();

            if (submitted || (lostFocus && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsItemHovered()))
            {
                string trimmed = _entityRenameBuffer.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    entity.Name = trimmed;
                _renamingEntityId = -1;
            }
            else if (escaped)
            {
                if (_isNewEntity)
                {
                    _context.ActiveScene?.Destroy(entity);
                    _context.Selection = null;
                }
                _renamingEntityId = -1;
            }
        }
        else
        {
            if (ImGui.IsItemClicked())
            {
                _context.Selection = entity;
            }

            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                OnEntityDoubleClicked?.Invoke(entity);
            }
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

                // Dropping a prefab onto an entity instantiates it as a child of that entity.
                var prefabPayload = ImGui.AcceptDragDropPayload("PREFAB_FILE");
                if (prefabPayload.NativePtr != null)
                {
                    string? path = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(prefabPayload.Data);
                    if (path != null) InstantiatePrefab(path, entity);
                }
            }
            ImGui.EndDragDropTarget();
        }

        bool entityDeleted = false;
        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Rename"))
            {
                _renamingEntityId = entity.Id;
                _entityRenameBuffer = entity.Name;
                _renameFocusPending = true;
                _isNewEntity = false;
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Move Up")) MoveEntityUp(entity);
            if (ImGui.MenuItem("Move Down")) MoveEntityDown(entity);
            ImGui.Separator();
            if (ImGui.MenuItem("Create Child Entity"))
            {
                var child = _context.ActiveScene!.Instantiate("Empty Entity");
                child.SetParent(entity);
                _context.Selection = child;
                _renamingEntityId = child.Id;
                _entityRenameBuffer = "Empty Entity";
                _renameFocusPending = true;
                _isNewEntity = true;
            }
            if (ImGui.MenuItem("Delete Entity"))
            {
                entityDeleted = true;
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

    // Keeps _rootOrder in sync with the scene: adds new root entities at the end, removes stale ones.
    private void SyncRootOrder()
    {
        var allRoot = _context.ActiveScene!.View<LabelComponent>()
            .Where(e => e.Parent == null)
            .Select(e => e.Id)
            .ToHashSet();
        _rootOrder.RemoveAll(id => !allRoot.Contains(id));
        foreach (int id in allRoot)
            if (!_rootOrder.Contains(id))
                _rootOrder.Add(id);
    }

    private void MoveEntityUp(Entity entity)
    {
        if (entity.Parent == null)
        {
            int idx = _rootOrder.IndexOf(entity.Id);
            if (idx > 0)
                (_rootOrder[idx], _rootOrder[idx - 1]) = (_rootOrder[idx - 1], _rootOrder[idx]);
        }
        else
        {
            var parent = entity.Parent.Value;
            var children = parent.GetComponent<RelationshipComponent>().Children;
            int idx = children.IndexOf(entity);
            if (idx > 0)
            {
                (children[idx], children[idx - 1]) = (children[idx - 1], children[idx]);
            }
            else
            {
                // First child: bubble up to grandparent level, placed before the current parent.
                Entity? grandparent = parent.Parent;
                if (grandparent == null)
                {
                    int parentRootIdx = _rootOrder.IndexOf(parent.Id);
                    entity.SetParent(null);
                    _rootOrder.Remove(entity.Id);
                    _rootOrder.Insert(Math.Max(0, parentRootIdx), entity.Id);
                }
                else
                {
                    var gpRel = grandparent.Value.GetComponent<RelationshipComponent>();
                    int parentIdx = gpRel.Children.IndexOf(parent);
                    entity.SetParent(grandparent);
                    // SetParent appends; move it to just before the former parent.
                    var gpChildren = gpRel.Children;
                    int entityIdx = gpChildren.LastIndexOf(entity);
                    if (entityIdx > parentIdx && parentIdx >= 0)
                    {
                        gpChildren.RemoveAt(entityIdx);
                        gpChildren.Insert(parentIdx, entity);
                    }
                }
            }
        }
    }

    private void MoveEntityDown(Entity entity)
    {
        if (entity.Parent == null)
        {
            int idx = _rootOrder.IndexOf(entity.Id);
            if (idx >= 0 && idx < _rootOrder.Count - 1)
                (_rootOrder[idx], _rootOrder[idx + 1]) = (_rootOrder[idx + 1], _rootOrder[idx]);
        }
        else
        {
            var children = entity.Parent.Value.GetComponent<RelationshipComponent>().Children;
            int idx = children.IndexOf(entity);
            if (idx >= 0 && idx < children.Count - 1)
                (children[idx], children[idx + 1]) = (children[idx + 1], children[idx]);
        }
    }
}
