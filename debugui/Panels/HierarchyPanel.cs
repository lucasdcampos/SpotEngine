using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.Scenes;
using Spot.Rendering;
using Spot.DebugUI.UI;

namespace Spot.DebugUI.Panels;

public class HierarchyPanel
{
    public Action<Entity>? OnEntityDoubleClicked;
    private readonly ISelectionContext _context;

    private int _renamingEntityId = -1;
    private string _entityRenameBuffer = "";
    private bool _renameFocusPending;
    private bool _isNewEntity;

    // Multi-selection anchor: the entity a Shift+click range extends from (the last plain/Ctrl click).
    private int _selectionAnchorId = -1;

    // A plain click inside a multi-selection is deferred: it collapses the selection to this entity on mouse
    // release, but only if no drag started in between — so the whole selection can be dragged as a group.
    private int _pendingClickEntityId = -1;

    // Set by a context-menu "Delete" on a multi-selection; the actual destroy is deferred to the end of
    // DrawContents so entities aren't torn down while the tree is still being drawn.
    private bool _deleteSelectionPending;

    // Explicit root-entity ordering (dictionary pools have no stable order for reordering).
    private readonly List<int> _rootOrder = new();

    // Tint for prefab-instance entities; matches the asset browser's prefab color.
    private static readonly Vector4 PrefabColor = new(0.40f, 0.82f, 0.92f, 1.0f);

    // Entity clipboard, shared across panel instances and scenes. Holds a serialized prefab (an entity
    // subtree). A cut also remembers the source entity so its first paste moves rather than copies.
    private static string? s_entityClipboardJson;
    private static Entity? s_cutEntity;

    public HierarchyPanel(ISelectionContext context)
    {
        _context = context;
    }

    /// <summary>Draws the entity tree as its own window (used by the runtime debug overlay).</summary>
    public void OnImGuiRender(ref bool open)
    {
        ImGui.Begin("Hierarchy", ref open, ImGuiWindowFlags.NoCollapse);
        DrawContents();
        ImGui.End();
    }

    /// <summary>
    /// Draws the entity tree into the current window. The editor hosts it inside a shared "Hierarchy" panel
    /// that switches between this and the UI widget tree, so this draws no window of its own.
    /// </summary>
    public void DrawContents()
    {
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

            // A multi-selection delete requested from a context menu is applied here, once the whole tree
            // has been drawn, so entities aren't destroyed mid-iteration.
            if (_deleteSelectionPending)
            {
                DeleteSelected();
                _deleteSelectionPending = false;
            }

            // Click on empty space clears the selection — but not while Ctrl/Shift is held, so those modifiers
            // keep extending the multi-selection rather than wiping it.
            if (ImGui.IsMouseDown(0) && ImGui.IsWindowHovered() && !ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift)
            {
                _context.Selection = null;
                _selectionAnchorId = -1;
            }

            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && _renamingEntityId == -1 && !ImGui.IsAnyItemActive())
            {
                Entity? sel = _context.Selection;
                if (ImGui.GetIO().KeyCtrl)
                {
                    if (sel != null && ImGui.IsKeyPressed(ImGuiKey.C)) CopyEntity(sel.Value);
                    else if (sel != null && ImGui.IsKeyPressed(ImGuiKey.X)) CutEntity(sel.Value);
                    else if (sel != null && ImGui.IsKeyPressed(ImGuiKey.D)) DuplicateSelected();
                    else if (ImGui.IsKeyPressed(ImGuiKey.V)) PasteEntity(sel);
                }
                else if (sel != null)
                {
                    if (ImGui.IsKeyPressed(ImGuiKey.F2))
                    {
                        _renamingEntityId = sel.Value.Id;
                        _entityRenameBuffer = sel.Value.Name;
                        _renameFocusPending = true;
                        _isNewEntity = false;
                    }
                    else if (ImGui.IsKeyPressed(ImGuiKey.Delete))
                    {
                        DeleteSelected();
                    }
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
                        ReparentDragged(payloadId, null);
                    }

                    var prefabPayload = ImGui.AcceptDragDropPayload("PREFAB_FILE");
                    if (prefabPayload.NativePtr != null)
                    {
                        string? path = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(prefabPayload.Data);
                        if (path != null) InstantiatePrefab(path, null);
                    }

                    // Dropping a model file imports it as a root entity hierarchy with materials applied.
                    var modelPayload = ImGui.AcceptDragDropPayload("MODEL_FILE");
                    if (modelPayload.NativePtr != null)
                    {
                        string? path = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(modelPayload.Data);
                        if (path != null) InstantiateModel(path, null);
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
                ImGui.Separator();
                if (ImGui.MenuItem("Paste", "Ctrl+V", false, s_entityClipboardJson != null))
                {
                    PasteEntity(null);
                }
                ImGui.EndPopup();
            }
        }

        ImGui.PopStyleVar(2);
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

    // Imports a model file as a faithful entity hierarchy (one entity per part, with the model's materials
    // extracted and applied) under an optional parent, and selects the new root. Failures are logged by the
    // instantiator and leave the scene unchanged.
    private void InstantiateModel(string path, Entity? parent)
    {
        var scene = _context.ActiveScene;
        if (scene == null) return;

        Entity? root = ModelInstantiator.Instantiate(scene, path, parent);
        if (root != null) _context.Selection = root.Value;
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

        ImGuiTreeNodeFlags flags = (IsSelected(entity) ? ImGuiTreeNodeFlags.Selected : 0) | ImGuiTreeNodeFlags.OpenOnArrow;
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
                HandleSelectionClick(entity);
            }

            // A deferred plain click inside a multi-selection collapses to this entity on release (when it
            // wasn't the start of a drag, which clears the pending click below).
            if (_pendingClickEntityId == entity.Id && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                if (ImGui.IsItemHovered())
                {
                    _context.Selection = entity;
                    _selectionAnchorId = entity.Id;
                }
                _pendingClickEntityId = -1;
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
            // Dragging one of several selected entities carries the whole selection; label reflects that.
            int selectedCount = _context.SelectedEntities.Count;
            ImGui.Text(IsSelected(entity) && selectedCount > 1 ? $"{selectedCount} entities" : entity.Name);
            _pendingClickEntityId = -1; // this press became a drag, so don't collapse the selection on release
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
                    ReparentDragged(payloadId, entity);
                }

                // Dropping a prefab onto an entity instantiates it as a child of that entity.
                var prefabPayload = ImGui.AcceptDragDropPayload("PREFAB_FILE");
                if (prefabPayload.NativePtr != null)
                {
                    string? path = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(prefabPayload.Data);
                    if (path != null) InstantiatePrefab(path, entity);
                }

                // Dropping a model onto an entity imports its hierarchy as a child of that entity.
                var modelPayload = ImGui.AcceptDragDropPayload("MODEL_FILE");
                if (modelPayload.NativePtr != null)
                {
                    string? path = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(modelPayload.Data);
                    if (path != null) InstantiateModel(path, entity);
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopupContextItem())
        {
            // Right-clicking an entity that isn't part of the current selection makes it the selection, so the
            // menu acts on what was clicked. Right-clicking within a multi-selection keeps the whole group.
            if (!IsSelected(entity))
            {
                _context.Selection = entity;
                _selectionAnchorId = entity.Id;
            }

            bool multi = _context.SelectedEntities.Count > 1;

            if (ImGui.MenuItem("Rename"))
            {
                _renamingEntityId = entity.Id;
                _entityRenameBuffer = entity.Name;
                _renameFocusPending = true;
                _isNewEntity = false;
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Copy", "Ctrl+C")) CopyEntity(entity);
            if (ImGui.MenuItem("Cut", "Ctrl+X")) CutEntity(entity);
            if (ImGui.MenuItem("Duplicate", "Ctrl+D")) DuplicateSelected();
            if (ImGui.MenuItem("Paste", "Ctrl+V", false, s_entityClipboardJson != null)) PasteEntity(entity);
            ImGui.Separator();
            if (ImGui.MenuItem("Move Up")) MoveSelected(up: true);
            if (ImGui.MenuItem("Move Down")) MoveSelected(up: false);
            ImGui.Separator();
            if (ImGui.MenuItem("Create Child Entity"))
            {
                var child = _context.ActiveScene!.Instantiate("Empty Entity");
                child.SetParent(entity);
                _context.Selection = child;
                _selectionAnchorId = child.Id;
                _renamingEntityId = child.Id;
                _entityRenameBuffer = "Empty Entity";
                _renameFocusPending = true;
                _isNewEntity = true;
            }
            if (ImGui.MenuItem(multi ? $"Delete {_context.SelectedEntities.Count} Entities" : "Delete Entity"))
            {
                // Deferred: destroying now would tear down entities the tree is still drawing this frame.
                _deleteSelectionPending = true;
            }
            ImGui.EndPopup();
        }

        if (opened)
        {
            if (hasChildren)
            {
                // Snapshot into a pooled list (reused across frames and recursion levels) so a reparent or
                // delete triggered while drawing can't mutate the collection mid-iteration — without the
                // fresh per-node allocation a ToList would cost every frame.
                List<Entity> children = RentEntityList();
                foreach (Entity child in entity.Children)
                {
                    children.Add(child);
                }

                foreach (Entity child in children)
                {
                    DrawEntityNode(child);
                }

                ReturnEntityList(children);
            }
            ImGui.TreePop();
        }
    }

    private void CopyEntity(Entity entity)
    {
        try
        {
            s_entityClipboardJson = Prefab.Serialize(entity);
            s_cutEntity = null;
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to copy entity: {0}", ex.Message);
        }
    }

    private void CutEntity(Entity entity)
    {
        try
        {
            s_entityClipboardJson = Prefab.Serialize(entity);
            s_cutEntity = entity;
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to cut entity: {0}", ex.Message);
        }
    }

    private void DuplicateEntity(Entity entity)
    {
        var scene = _context.ActiveScene;
        if (scene == null) return;
        try
        {
            // Duplicate as a sibling (same parent) of the source.
            Entity? copy = Prefab.InstantiateInto(scene, Prefab.Serialize(entity), entity.Parent);
            if (copy != null)
            {
                // Give the copy a distinct name so duplicates don't pile up under identical labels.
                Entity copyEntity = copy.Value;
                copyEntity.Name = MakeUniqueSiblingName(scene, entity.Parent, entity.Name);
                _context.Selection = copyEntity;
            }
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to duplicate entity: {0}", ex.Message);
        }
    }

    // Pastes the clipboard entity under the given parent (or at the root when null). A pending cut is
    // consumed: the source is removed after a successful paste, turning the copy into a move.
    private void PasteEntity(Entity? parent)
    {
        var scene = _context.ActiveScene;
        if (scene == null || string.IsNullOrEmpty(s_entityClipboardJson)) return;

        // A cut can't be moved into itself or one of its own descendants.
        if (s_cutEntity != null && parent != null && IsSelfOrDescendant(parent.Value, s_cutEntity.Value))
        {
            return;
        }

        try
        {
            Entity? pasted = Prefab.InstantiateInto(scene, s_entityClipboardJson, parent);
            if (pasted == null) return;
            _context.Selection = pasted.Value;

            if (s_cutEntity != null)
            {
                if (s_cutEntity.Value.Scene == scene)
                {
                    scene.Destroy(s_cutEntity.Value);
                }
                s_cutEntity = null;
                s_entityClipboardJson = null; // a cut is consumed by its paste
            }
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to paste entity: {0}", ex.Message);
        }
    }

    // Builds a name that doesn't collide with the target parent's existing children ("Enemy" -> "Enemy (1)",
    // then "Enemy (2)"...). An existing " (N)" suffix on the source is stripped first so duplicating
    // "Enemy (1)" yields "Enemy (2)" rather than "Enemy (1) (1)".
    private static string MakeUniqueSiblingName(Scene scene, Entity? parent, string baseName)
    {
        var siblingNames = (parent != null
                ? parent.Value.Children
                : scene.View<LabelComponent>().Where(e => e.Parent == null))
            .Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);

        string stem = baseName;
        var suffix = System.Text.RegularExpressions.Regex.Match(baseName, @"^(.*?)\s\((\d+)\)$");
        if (suffix.Success) stem = suffix.Groups[1].Value;

        for (int n = 1; ; n++)
        {
            string candidate = $"{stem} ({n})";
            if (!siblingNames.Contains(candidate)) return candidate;
        }
    }

    private static bool IsSelfOrDescendant(Entity node, Entity ancestor)
    {
        Entity? current = node;
        while (current != null)
        {
            if (current.Value == ancestor) return true;
            current = current.Value.Parent;
        }
        return false;
    }

    // Reused across frames so SyncRootOrder doesn't allocate a set (and a LINQ chain) every frame.
    private readonly HashSet<int> _rootScratch = new();

    // Pool of scratch child lists, reused across frames and recursion depth so the tree draw doesn't
    // allocate a list per expanded parent node every frame. Depth-bounded by its LIFO rent/return.
    private readonly Stack<List<Entity>> _entityListPool = new();

    private List<Entity> RentEntityList()
    {
        List<Entity> list = _entityListPool.Count > 0 ? _entityListPool.Pop() : new List<Entity>();
        list.Clear();
        return list;
    }

    private void ReturnEntityList(List<Entity> list) => _entityListPool.Push(list);

    // Keeps _rootOrder in sync with the scene: adds new root entities at the end, removes stale ones.
    private void SyncRootOrder()
    {
        _rootScratch.Clear();
        foreach (Entity e in _context.ActiveScene!.View<LabelComponent>())
        {
            if (e.Parent == null)
            {
                _rootScratch.Add(e.Id);
            }
        }

        for (int i = _rootOrder.Count - 1; i >= 0; i--)
        {
            if (!_rootScratch.Contains(_rootOrder[i]))
            {
                _rootOrder.RemoveAt(i);
            }
        }

        foreach (int id in _rootScratch)
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

    // --- Multi-selection ----------------------------------------------------------------------------------

    private bool IsSelected(Entity entity)
    {
        foreach (Entity e in _context.SelectedEntities)
            if (e == entity) return true;
        return false;
    }

    // Turns a click on an entity row into a selection change, honoring Ctrl (toggle one) and Shift (range).
    private void HandleSelectionClick(Entity entity)
    {
        var io = ImGui.GetIO();
        if (io.KeyShift && _selectionAnchorId != -1)
        {
            SelectRange(_selectionAnchorId, entity.Id);
            // The anchor stays fixed so successive Shift+clicks grow/shrink the same range.
        }
        else if (io.KeyCtrl)
        {
            ToggleSelection(entity);
            _selectionAnchorId = entity.Id;
        }
        else if (IsSelected(entity) && _context.SelectedEntities.Count > 1)
        {
            // Defer collapsing so a drag starting from within the selection carries the whole group.
            _pendingClickEntityId = entity.Id;
        }
        else
        {
            _context.Selection = entity;
            _selectionAnchorId = entity.Id;
        }
    }

    private void ToggleSelection(Entity entity)
    {
        var list = new List<Entity>(_context.SelectedEntities);
        int idx = list.FindIndex(e => e == entity);
        if (idx >= 0) list.RemoveAt(idx);
        else list.Add(entity); // newly added entity becomes the primary (last) selection
        _context.SetSelectedEntities(list);
    }

    // Selects every entity between the anchor and the clicked row in display (flattened tree) order.
    private void SelectRange(int anchorId, int clickedId)
    {
        var flat = FlattenTree();
        int a = flat.FindIndex(e => e.Id == anchorId);
        int b = flat.FindIndex(e => e.Id == clickedId);
        if (a < 0 || b < 0)
        {
            _context.Selection = new Entity(clickedId, _context.ActiveScene!);
            _selectionAnchorId = clickedId;
            return;
        }
        if (a > b) (a, b) = (b, a);
        var range = flat.GetRange(a, b - a + 1);
        // Keep the clicked entity as the primary selection (last), so the Inspector/gizmo follow the cursor.
        if (range.Count > 0 && range[^1].Id != clickedId) range.Reverse();
        _context.SetSelectedEntities(range);
    }

    // Flattens the whole tree into its on-screen top-to-bottom order (root order, each node followed by its
    // descendants), which is what Shift+range selection walks over.
    private List<Entity> FlattenTree()
    {
        var result = new List<Entity>();
        var scene = _context.ActiveScene;
        if (scene == null) return result;
        foreach (int rootId in _rootOrder)
            AppendSubtree(new Entity(rootId, scene), result);
        return result;
    }

    private static void AppendSubtree(Entity entity, List<Entity> into)
    {
        into.Add(entity);
        foreach (Entity child in entity.Children)
            AppendSubtree(child, into);
    }

    // The selected entities that have no selected ancestor. Operations like delete/reparent act on these so a
    // selected child isn't handled twice (it moves or is destroyed together with its selected parent).
    private List<Entity> TopLevelSelected()
    {
        var selected = _context.SelectedEntities;
        var ids = new HashSet<int>();
        foreach (Entity e in selected) ids.Add(e.Id);

        var result = new List<Entity>();
        foreach (Entity e in selected)
        {
            bool hasSelectedAncestor = false;
            Entity? p = e.Parent;
            while (p != null)
            {
                if (ids.Contains(p.Value.Id)) { hasSelectedAncestor = true; break; }
                p = p.Value.Parent;
            }
            if (!hasSelectedAncestor) result.Add(e);
        }
        return result;
    }

    private void DeleteSelected()
    {
        var scene = _context.ActiveScene;
        if (scene == null) return;
        foreach (Entity e in TopLevelSelected())
            scene.Destroy(e);
        _context.Selection = null;
        _selectionAnchorId = -1;
    }

    // Duplicates every top-level selected entity as a sibling and selects the copies. Falls back to the
    // single-entity path (which also handles inline naming) when only one entity is selected.
    private void DuplicateSelected()
    {
        if (_context.SelectedEntities.Count <= 1)
        {
            if (_context.Selection != null) DuplicateEntity(_context.Selection.Value);
            return;
        }

        var scene = _context.ActiveScene;
        if (scene == null) return;

        var copies = new List<Entity>();
        foreach (Entity e in TopLevelSelected())
        {
            try
            {
                Entity? copy = Prefab.InstantiateInto(scene, Prefab.Serialize(e), e.Parent);
                if (copy != null)
                {
                    Entity c = copy.Value;
                    c.Name = MakeUniqueSiblingName(scene, e.Parent, e.Name);
                    copies.Add(c);
                }
            }
            catch (Exception ex)
            {
                Spot.Core.Log.Error("Failed to duplicate entity: {0}", ex.Message);
            }
        }

        if (copies.Count > 0) _context.SetSelectedEntities(copies);
    }

    // Moves the whole selection one step up or down within each entity's own sibling list. A single selection
    // keeps the richer single-entity behavior (first child bubbles up to the grandparent).
    private void MoveSelected(bool up)
    {
        var selected = _context.SelectedEntities;
        if (selected.Count == 0) return;
        if (selected.Count == 1)
        {
            if (up) MoveEntityUp(selected[0]); else MoveEntityDown(selected[0]);
            return;
        }

        var ids = new HashSet<int>();
        foreach (Entity e in selected) ids.Add(e.Id);

        // Root-level entities reorder within _rootOrder; children reorder within their parent's child list.
        ReorderBlock(_rootOrder, static id => id, ids, up);

        var visitedParents = new HashSet<int>();
        foreach (Entity e in selected)
        {
            Entity? parent = e.Parent;
            if (parent == null) continue;
            if (!visitedParents.Add(parent.Value.Id)) continue;
            var children = parent.Value.GetComponent<RelationshipComponent>().Children;
            ReorderBlock(children, static ch => ch.Id, ids, up);
        }
    }

    // Shifts every selected item one slot toward the top (or bottom) of the list, swapping only with an
    // unselected neighbor so a contiguous block stays together and stops at the list boundary.
    private static void ReorderBlock<T>(List<T> list, Func<T, int> idOf, HashSet<int> selected, bool up)
    {
        if (up)
        {
            for (int i = 1; i < list.Count; i++)
                if (selected.Contains(idOf(list[i])) && !selected.Contains(idOf(list[i - 1])))
                    (list[i], list[i - 1]) = (list[i - 1], list[i]);
        }
        else
        {
            for (int i = list.Count - 2; i >= 0; i--)
                if (selected.Contains(idOf(list[i])) && !selected.Contains(idOf(list[i + 1])))
                    (list[i], list[i + 1]) = (list[i + 1], list[i]);
        }
    }

    // Reparents a dragged entity to newParent (null = scene root). When the dragged entity is part of a
    // multi-selection, the whole (top-level) selection moves together; a drop onto self or a descendant is
    // skipped so the hierarchy can't be knotted.
    private void ReparentDragged(int draggedId, Entity? newParent)
    {
        var scene = _context.ActiveScene;
        if (scene == null) return;

        Entity dragged = new Entity(draggedId, scene);
        IEnumerable<Entity> toMove = IsSelected(dragged) && _context.SelectedEntities.Count > 1
            ? TopLevelSelected()
            : new[] { dragged };

        foreach (Entity e in toMove)
        {
            if (newParent != null && IsSelfOrDescendant(newParent.Value, e)) continue;
            e.SetParent(newParent);
        }
    }
}
