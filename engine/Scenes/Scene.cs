using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Spot.Core;
using Spot.Events;
using Spot.Physics;
using Spot.Rendering;
using Spot.UI;

namespace Spot.Scenes;

/// <summary>
/// A game scene: both a container of entities/components and a switchable screen with its own
/// lifecycle. Derive from it to build a screen (a menu, a level, a test), overriding the lifecycle
/// hooks, and use the entity API to populate it. The <see cref="SceneManager"/> drives the active
/// scene; components are plain data queried by systems (see <see cref="RenderSystem"/>).
/// </summary>
public class Scene
{
    // The scene's entity/component store and runtime physics, split into focused collaborators. The scene
    // keeps the public API and delegates storage, queries, hierarchy-active memoization, and physics here.
    private readonly EntityRegistry _registry;
    private readonly ScenePhysics _physics;
    private readonly HashSet<int> _pendingDestroy = new();

    /// <summary>
    /// The ordered play-mode systems this scene runs each frame from <see cref="UpdateRuntime"/>. Every
    /// scene starts with the engine's built-in systems registered; use <see cref="RegisterSystem(ISystem)"/>
    /// to add your own. See <see cref="ISystem"/> and <see cref="SystemOrder"/>.
    /// </summary>
    public SystemRegistry Systems { get; } = new();

    private UIRoot? _ui;

    /// <summary>
    /// This scene's screen-space UI tree. Built in code from scripts (see <c>EntityBehaviour.UI</c>): add
    /// <see cref="Widget"/>s to it and the engine lays them out, routes pointer input each play-mode frame,
    /// and draws them as the final pass. Created on first access, so scenes without UI cost nothing.
    /// </summary>
    public UIRoot UI => _ui ??= new UIRoot();

    // The UI root without creating one — lets the render system and update tick skip scenes that never built UI.
    internal UIRoot? UIRootOrNull => _ui;

    /// <summary>
    /// Creates a scene with the engine's built-in play-mode systems registered (character controllers, 2D
    /// and 3D physics, animation, particles, audio, and scripts, in that order). Register additional systems
    /// from a subclass constructor or <see cref="OnEnter"/> via <see cref="RegisterSystem(ISystem)"/>.
    /// </summary>
    public Scene()
    {
        _registry = new EntityRegistry(this);
        _physics = new ScenePhysics(this);

        Systems.Add(new DelegateSystem(SystemOrder.UICanvas, UICanvasSystem.Update));
        Systems.Add(new DelegateSystem(SystemOrder.CharacterController, CharacterController3DSystem.Update));
        Systems.Add(new DelegateSystem(SystemOrder.FixedUpdate, ScriptSystem.FixedUpdate));
        Systems.Add(new DelegateSystem(SystemOrder.Physics2D, static (scene, dt) => scene.StepPhysics2D(dt)));
        Systems.Add(new DelegateSystem(SystemOrder.Physics3D, static (scene, dt) => scene.StepPhysics3D(dt)));
        Systems.Add(new DelegateSystem(SystemOrder.Animation, AnimationSystem.Update));
        Systems.Add(new DelegateSystem(SystemOrder.Particles, ParticleSystem.Update));
        Systems.Add(new DelegateSystem(SystemOrder.Audio, AudioSystem.Update));
        Systems.Add(new DelegateSystem(SystemOrder.Scripts, ScriptSystem.Update));
    }

    /// <summary>
    /// Registers a custom play-mode system to run each frame alongside the built-ins. See <see cref="ISystem"/>
    /// for the contract and <see cref="SystemOrder"/> for how to place it relative to the built-in systems.
    /// </summary>
    /// <param name="system">The system to register.</param>
    public void RegisterSystem(ISystem system) => Systems.Add(system);

    /// <summary>
    /// Called once when the scene becomes active. Create resources and entities here.
    /// </summary>
    public virtual void OnEnter()
    {
        foreach (var entity in View<CameraComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var cc = GetComponent<CameraComponent>(entity);
            if (!cc.Enabled) continue;
            if (!cc.FixedAspectRatio)
            {
                cc.SetViewportSize(Spot.Core.Display.Width, Spot.Core.Display.Height);
            }
        }
    }

    /// <summary>
    /// Called every frame while the scene is active.
    /// </summary>
    /// <param name="deltaTime">The elapsed time in seconds since the previous frame.</param>
    public virtual void OnUpdate(float deltaTime)
    {
    }

    /// <summary>
    /// Called every frame in play mode to run scene logic (scripts, physics).
    /// </summary>
    public void UpdateRuntime(float deltaTime)
    {
        OnUpdate(deltaTime);
        Systems.Update(this, deltaTime);
        TickUI();
        FlushDestroyed();
    }

    // Routes pointer input through the UI tree after scripts have (re)built it this frame. Skipped entirely
    // until a scene actually has UI, so the common case touches neither the window nor the input statics.
    private void TickUI()
    {
        if (_ui is null || _ui.Children.Count == 0) return;

        _ui.Update(
            Spot.Core.Display.Width,
            Spot.Core.Display.Height,
            Input.MousePosition,
            Input.GetMouseButton(MouseButton.Left),
            Input.GetMouseButtonDown(MouseButton.Left),
            Input.GetMouseButtonUp(MouseButton.Left));
    }

    // Steps the 3D physics simulation and dispatches its contacts; the built-in Physics3D ISystem triggers
    // this in order (see SystemOrder.Physics3D). The backend and dispatcher live on ScenePhysics.
    private void StepPhysics3D(float deltaTime) => _physics.Step3D(deltaTime);

    // Steps the 2D physics simulation and dispatches its contacts; the built-in Physics2D ISystem triggers
    // this in order (see SystemOrder.Physics2D). The backend and dispatcher live on ScenePhysics.
    private void StepPhysics2D(float deltaTime) => _physics.Step2D(deltaTime);

    /// <summary>
    /// Casts a ray against this scene's 3D physics and returns the closest hit within
    /// <paramref name="maxDistance"/>. Only meaningful during play mode (when the simulation is live).
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit) =>
        _physics.Raycast(origin, direction, maxDistance, out hit);

    /// <summary>
    /// Casts a ray against this scene's 2D physics (XY plane) and returns the closest hit within
    /// <paramref name="maxDistance"/>. Only meaningful during play mode (when the simulation is live).
    /// </summary>
    public bool Raycast2D(Vector2 origin, Vector2 direction, float maxDistance, out RaycastHit2D hit) =>
        _physics.Raycast2D(origin, direction, maxDistance, out hit);

    /// <summary>
    /// Called every frame to render the scene, after the screen is cleared.
    /// </summary>
    public virtual void OnRender()
    {
        Matrix4x4? viewProjection = null;
        Vector3 cameraPosition = Vector3.Zero;
        Vector4 clearColor = new(0.1f, 0.1f, 0.1f, 1.0f);
        bool is3D = false;

        foreach (var entity in View<CameraComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var cc = entity.GetComponent<CameraComponent>();
            if (!cc.Enabled) continue;
            if (cc.Primary)
            {
                if (HasComponent<TransformComponent>(entity))
                {
                    var transform = GetComponent<TransformComponent>(entity);
                    if (!transform.Enabled) continue;
                    viewProjection = cc.GetViewProjection(transform);
                    cameraPosition = transform.WorldPosition;
                    is3D = cc.ProjectionType == SceneCameraProjection.Perspective;
                }
                clearColor = cc.BackgroundColor;
                break;
            }
        }

        if (viewProjection.HasValue)
        {
            Renderer.SetClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
            // Renderer.Clear() is already called in Application.cs loop before SceneManager.Render,
            // but we want to clear with our own color, so we call it again.
            Renderer.Clear();

            if (is3D)
            {
                Renderer.SetDepthTest(true);
                Renderer.SetFaceCulling(true);
            }

            SceneRenderer.Render(this, viewProjection.Value, cameraPosition);

            if (is3D)
            {
                Renderer.SetDepthTest(false);
                Renderer.SetFaceCulling(false);
            }
        }
    }

    /// <summary>
    /// Returns true if the scene has a primary camera that would actually render this frame — active in
    /// the hierarchy, enabled, and carrying an enabled transform (the same conditions <see cref="OnRender"/>
    /// uses to pick a camera). The editor uses this to warn when the Game view would otherwise be blank.
    /// </summary>
    public bool HasActivePrimaryCamera()
    {
        foreach (var entity in View<CameraComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var cc = entity.GetComponent<CameraComponent>();
            if (!cc.Enabled || !cc.Primary) continue;
            if (!HasComponent<TransformComponent>(entity)) continue;
            if (!GetComponent<TransformComponent>(entity).Enabled) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Called every frame to build the scene's ImGui user interface.
    /// </summary>
    public virtual void OnImGuiRender()
    {
        ScriptSystem.ImGuiRender(this);
    }

    /// <summary>
    /// Called for each window/input event the engine did not consume. Set <see cref="Event.Handled"/>
    /// to stop further processing. For continuous input, prefer polling <see cref="Input"/> in
    /// <see cref="OnUpdate"/>.
    /// </summary>
    /// <param name="e">The event.</param>
    public virtual void OnEvent(Event e)
    {
        var dispatcher = new EventDispatcher(e);
        dispatcher.Dispatch<WindowResizeEvent>(OnWindowResize);
    }

    private bool OnWindowResize(WindowResizeEvent e)
    {
        foreach (var entity in View<CameraComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var cc = GetComponent<CameraComponent>(entity);
            if (!cc.Enabled) continue;
            if (!cc.FixedAspectRatio)
            {
                cc.SetViewportSize(e.Width, e.Height);
            }
        }
        return false;
    }

    /// <summary>
    /// Called once when the scene is being replaced. Dispose resources here.
    /// </summary>
    public virtual void OnExit()
    {
    }

    /// <summary>
    /// Creates a new entity with a <see cref="LabelComponent"/> and a <see cref="TransformComponent"/>. Safe to
    /// call at any time, including from a script.
    /// </summary>
    /// <param name="name">The entity name.</param>
    /// <returns>The new entity.</returns>
    public Entity Instantiate(string name = "Entity")
    {
        int id = _registry.CreateEntity();

        var entity = new Entity(id, this);
        entity.AddComponent(new LabelComponent(name));
        entity.AddComponent(new RelationshipComponent());
        entity.AddComponent(new TransformComponent());
        return entity;
    }

    /// <summary>
    /// Marks an entity for destruction. The entity and its components are removed at the end of the
    /// current frame, so it is safe to call from a script (even on the entity running the script).
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    public void Destroy(Entity entity) => _pendingDestroy.Add(entity.Id);

    /// <summary>
    /// Removes every entity and component from the scene, resetting it to an empty state. Entity ids
    /// are reset so a subsequent re-population is deterministic. Used by the editor to re-hydrate a
    /// scene in place (for example when restoring an undo snapshot) without swapping the instance.
    /// </summary>
    internal void Clear()
    {
        _registry.Clear();
        _pendingDestroy.Clear();
        TeardownPhysics();
    }

    /// <summary>
    /// Disposes the runtime physics backend, if one was built. Called by the <see cref="SceneManager"/>
    /// when the scene is exited so the native-free simulation and its buffers are released. Safe to call
    /// when no backend exists; a later <see cref="UpdateRuntime"/> rebuilds one on demand.
    /// </summary>
    internal void TeardownPhysics() => _physics.Teardown();

    /// <summary>
    /// Returns a handle to the entity with the given id if it is still alive, otherwise
    /// <see langword="null"/>. Convenience for callers that hold a bare id (such as the editor
    /// remapping a selection after a snapshot restore).
    /// </summary>
    internal Entity? EntityById(int? id) =>
        id is int value && _registry.Contains(value) ? new Entity(value, this) : null;

    /// <summary>
    /// Destroys all entities marked with <see cref="Destroy"/> since the last flush. Called by the
    /// engine at the end of each frame.
    /// </summary>
    internal void FlushDestroyed()
    {
        if (_pendingDestroy.Count == 0)
        {
            return;
        }

        foreach (int id in _pendingDestroy)
        {
            DestroyImmediate(id);
        }

        _pendingDestroy.Clear();
    }

    private void DestroyImmediate(int id)
    {
        var entity = new Entity(id, this);
        if (entity.TryGetComponent(out RelationshipComponent? rel))
        {
            entity.SetParent(null);
            foreach (var child in rel.Children.ToList())
            {
                DestroyImmediate(child.Id);
            }
        }

        if (_registry.TryGet(typeof(ScriptComponent), id, out object? value))
        {
            foreach (EntityBehaviour script in ((ScriptComponent)value).Scripts)
            {
                if (!script.Started)
                {
                    continue;
                }

                // A still-enabled script gets OnDisable before OnDestroy, mirroring OnEnable/OnCreate. Both
                // are guarded so a throwing teardown hook cannot abort destruction of the rest of the tree.
                if (script.ActiveLastFrame)
                {
                    script.ActiveLastFrame = false;
                    try
                    {
                        script.OnDisable();
                    }
                    catch (Exception ex)
                    {
                        Log.CoreError("Script '{0}' threw from OnDisable; ignoring. {1}", script.GetType().Name, ex);
                    }
                }

                try
                {
                    script.OnDestroy();
                }
                catch (Exception ex)
                {
                    Log.CoreError("Script '{0}' threw from OnDestroy; ignoring. {1}", script.GetType().Name, ex);
                }
            }
        }

        _registry.RemoveEntity(id);
    }

    /// <summary>
    /// Returns every entity that has a component of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The component type to match.</typeparam>
    /// <returns>A snapshot of the matching entities, safe to modify the scene while iterating.</returns>
    public IReadOnlyList<Entity> View<T>()
        where T : class =>
        _registry.View<T>();

    /// <summary>
    /// Returns every entity that has components of both <typeparamref name="T1"/> and <typeparamref name="T2"/>.
    /// </summary>
    /// <typeparam name="T1">The first component type to match.</typeparam>
    /// <typeparam name="T2">The second component type to match.</typeparam>
    /// <returns>A snapshot of the matching entities, safe to modify the scene while iterating.</returns>
    public IReadOnlyList<Entity> View<T1, T2>()
        where T1 : class
        where T2 : class =>
        _registry.View<T1, T2>();

    /// <summary>
    /// Returns every entity that has a component of type <typeparamref name="T"/> which is both enabled and
    /// on an entity active in the hierarchy — the standard gate a system applies before touching a
    /// component — paired with that component. The result is a snapshot, so it is safe to spawn or destroy
    /// entities while iterating (for example from a script or a custom <see cref="ISystem"/>).
    /// </summary>
    /// <typeparam name="T">The component type to match.</typeparam>
    public IReadOnlyList<(Entity Entity, T Component)> ViewActive<T>()
        where T : Component =>
        _registry.ViewActive<T>();

    /// <summary>
    /// Moves every persistent root entity (marked via <see cref="Entity.DontDestroyOnLoad"/>) and its
    /// subtree from this scene into <paramref name="target"/>, preserving live component and script
    /// state. Called by the <see cref="SceneManager"/> during a scene switch, before this scene is
    /// torn down, so persistent objects carry over rather than being destroyed with the scene.
    /// </summary>
    internal void MigratePersistentEntitiesTo(Scene target)
    {
        if (ReferenceEquals(this, target))
        {
            return;
        }

        // Snapshot the roots first: adopting mutates this scene's entity set as it goes.
        var roots = new List<int>();
        foreach (int id in _registry.EntityIds)
        {
            if (GetComponent(new Entity(id, this), typeof(LabelComponent)) is LabelComponent label &&
                label.Persistent &&
                new Entity(id, this).Parent is null)
            {
                roots.Add(id);
            }
        }

        foreach (int rootId in roots)
        {
            target.AdoptSubtree(this, rootId);
        }
    }

    /// <summary>
    /// Adopts the entity <paramref name="rootId"/> and all of its descendants from
    /// <paramref name="source"/> into this scene, reusing the existing component and script instances so
    /// their runtime state is preserved. Entity ids are re-minted in this scene and every stored entity
    /// handle (transform, relationship, script) is rebound to the new ids and this scene.
    /// </summary>
    private void AdoptSubtree(Scene source, int rootId)
    {
        var order = new List<int>();
        CollectSubtree(source, rootId, order);

        // Pass 1: re-mint ids and move each entity's component instances into this scene's registry.
        var remap = new Dictionary<int, int>(order.Count);
        foreach (int oldId in order)
        {
            int newId = _registry.CreateEntity();
            remap[oldId] = newId;
            source._registry.MoveEntityComponentsTo(oldId, _registry, newId);
        }
        source._registry.InvalidateCaches();
        _registry.InvalidateCaches();

        // Pass 2: rebind every stored entity handle now that all new ids exist.
        foreach (int newId in remap.Values)
        {
            var entity = new Entity(newId, this);

            if (TryGetComponent(entity, out TransformComponent? transform))
            {
                transform.Entity = entity;
            }

            if (TryGetComponent(entity, out LabelComponent? label))
            {
                label.OwnerScene = this;
            }

            if (TryGetComponent(entity, out RelationshipComponent? rel))
            {
                rel.Parent = Remap(rel.Parent, remap);
                for (int i = 0; i < rel.Children.Count; i++)
                {
                    Entity? mapped = Remap(rel.Children[i], remap);
                    if (mapped.HasValue)
                    {
                        rel.Children[i] = mapped.Value;
                    }
                }
            }

            if (TryGetComponent(entity, out ScriptComponent? scripts))
            {
                foreach (EntityBehaviour script in scripts.Scripts)
                {
                    script.Entity = entity;
                }
            }
        }
    }

    // Rebinds an entity handle to this scene using the id remap; a handle outside the migrated subtree
    // (which for a root's parent means it stays behind) becomes null so nothing dangles into the old scene.
    private Entity? Remap(Entity? handle, Dictionary<int, int> remap) =>
        handle is Entity value && remap.TryGetValue(value.Id, out int newId)
            ? new Entity(newId, this)
            : null;

    // Depth-first list of an entity id and its descendants, gathered from the source scene's hierarchy
    // before any migration mutates it.
    private static void CollectSubtree(Scene source, int id, List<int> order)
    {
        order.Add(id);
        if (source.GetComponent(new Entity(id, source), typeof(RelationshipComponent)) is RelationshipComponent rel)
        {
            foreach (Entity child in rel.Children.ToList())
            {
                CollectSubtree(source, child.Id, order);
            }
        }
    }

    /// <summary>
    /// Returns the first entity whose name equals <paramref name="name"/> (ordinal comparison), or
    /// <see langword="null"/> if none matches. Names are not guaranteed unique; the first match wins.
    /// </summary>
    /// <param name="name">The entity name to search for.</param>
    public Entity? Find(string name)
    {
        foreach (Entity entity in View<LabelComponent>())
        {
            if (string.Equals(GetComponent<LabelComponent>(entity).Name, name, StringComparison.Ordinal))
            {
                return entity;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the first entity tagged <paramref name="tag"/>, or <see langword="null"/> if none matches.
    /// </summary>
    /// <param name="tag">The tag to search for.</param>
    public Entity? FindByTag(string tag)
    {
        foreach (Entity entity in View<LabelComponent>())
        {
            if (string.Equals(GetComponent<LabelComponent>(entity).Tag, tag, StringComparison.Ordinal))
            {
                return entity;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns every entity tagged <paramref name="tag"/> as a snapshot list, safe to modify the scene
    /// while iterating.
    /// </summary>
    /// <param name="tag">The tag to search for.</param>
    public IReadOnlyList<Entity> FindAllByTag(string tag)
    {
        var result = new List<Entity>();
        foreach (Entity entity in View<LabelComponent>())
        {
            if (string.Equals(GetComponent<LabelComponent>(entity).Tag, tag, StringComparison.Ordinal))
            {
                result.Add(entity);
            }
        }

        return result;
    }

    internal bool IsAlive(Entity entity) => _registry.Contains(entity.Id);

    /// <summary>
    /// Drops the cached hierarchy-active results so the next query recomputes. Called whenever something
    /// that affects active state changes: an entity's Enabled flag, a reparent, or a component add/remove.
    /// </summary>
    internal void InvalidateHierarchyActive() => _registry.InvalidateHierarchyActive();

    internal bool IsActiveInHierarchy(int entityId) => _registry.IsActiveInHierarchy(entityId);

    // Component access delegates to the registry, which stores each component under its type and invalidates
    // the query and hierarchy caches on mutation. The scene first wires the two components that need a
    // back-reference (a transform to its entity, a label to its owning scene) before storing them.

    internal T AddComponent<T>(Entity entity, T component)
        where T : class
    {
        WireComponent(entity, component);
        _registry.Set(typeof(T), entity.Id, component);
        return component;
    }

    internal T GetComponent<T>(Entity entity)
        where T : class =>
        _registry.Get<T>(entity.Id);

    internal bool TryGetComponent<T>(Entity entity, [NotNullWhen(true)] out T? component)
        where T : class =>
        _registry.TryGet(entity.Id, out component);

    internal bool HasComponent<T>(Entity entity)
        where T : class =>
        _registry.Has<T>(entity.Id);

    internal void RemoveComponent<T>(Entity entity)
        where T : class =>
        _registry.Remove<T>(entity.Id);

    // Non-generic component access, keyed by runtime type, for callers that only know a component's Type at
    // runtime (e.g. the editor's reflection-based inspector).

    internal bool HasComponent(Entity entity, Type type) => _registry.Has(type, entity.Id);

    internal object? GetComponent(Entity entity, Type type) => _registry.Get(type, entity.Id);

    internal bool TryGetComponent(Entity entity, Type type, [NotNullWhen(true)] out object? component) =>
        _registry.TryGet(type, entity.Id, out component);

    internal Component AddComponent(Entity entity, Component component)
    {
        WireComponent(entity, component);
        _registry.Set(component.GetType(), entity.Id, component);
        return component;
    }

    internal void RemoveComponent(Entity entity, Type type) => _registry.Remove(type, entity.Id);

    // Gives a transform its owning entity and a label its owning scene so components can navigate back to
    // the scene graph. Other component types need no wiring.
    private void WireComponent(Entity entity, object component)
    {
        if (component is TransformComponent transform)
        {
            transform.Entity = entity;
        }
        else if (component is LabelComponent label)
        {
            label.OwnerScene = this;
        }
    }
}
