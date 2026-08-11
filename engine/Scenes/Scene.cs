using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Spot.Core;
using Spot.Events;
using Spot.Physics;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// A game scene: both a container of entities/components and a switchable screen with its own
/// lifecycle. Derive from it to build a screen (a menu, a level, a test), overriding the lifecycle
/// hooks, and use the entity API to populate it. The <see cref="SceneManager"/> drives the active
/// scene; components are plain data queried by systems (see <see cref="RenderSystem"/>).
/// </summary>
public class Scene
{
    private readonly HashSet<int> _entities = new();
    private readonly Dictionary<Type, Dictionary<int, object>> _pools = new();
    private readonly HashSet<int> _pendingDestroy = new();
    private int _nextId = 1;
    private IPhysics3D? _physics3D;
    private CollisionDispatcher? _collisions;

    /// <summary>
    /// The ordered play-mode systems this scene runs each frame from <see cref="UpdateRuntime"/>. Every
    /// scene starts with the engine's built-in systems registered; use <see cref="RegisterSystem(ISystem)"/>
    /// to add your own. See <see cref="ISystem"/> and <see cref="SystemOrder"/>.
    /// </summary>
    public SystemRegistry Systems { get; } = new();

    /// <summary>
    /// Creates a scene with the engine's built-in play-mode systems registered (character controllers, 2D
    /// and 3D physics, animation, particles, audio, and scripts, in that order). Register additional systems
    /// from a subclass constructor or <see cref="OnEnter"/> via <see cref="RegisterSystem(ISystem)"/>.
    /// </summary>
    public Scene()
    {
        Systems.Add(new DelegateSystem(SystemOrder.CharacterController, CharacterController3DSystem.Update));
        Systems.Add(new DelegateSystem(SystemOrder.Physics2D, Physics2DSystem.Update));
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
        var window = Spot.Core.Application.Instance.Window;
        foreach (var entity in View<CameraComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var cc = GetComponent<CameraComponent>(entity);
            if (!cc.Enabled) continue;
            if (!cc.FixedAspectRatio)
            {
                cc.SetViewportSize(window.Width, window.Height);
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
        FlushDestroyed();
    }

    /// <summary>
    /// Runs the 3D physics step and dispatches its collision events. Wrapped so the physics backend and the
    /// contacts it produces stay encapsulated on the scene while the built-in physics <see cref="ISystem"/>
    /// simply triggers it in order (see <see cref="SystemOrder.Physics3D"/>).
    /// </summary>
    private void StepPhysics3D(float deltaTime)
    {
        IPhysics3D physics = EnsurePhysics3D();
        physics.Step(this, deltaTime);
        (_collisions ??= new CollisionDispatcher()).Dispatch(physics.Contacts);
    }

    /// <summary>
    /// Lazily builds this scene's 3D physics backend from <see cref="PhysicsSettings.Backend"/>. If the
    /// preferred backend fails to initialize, falls back to the legacy AABB solver so play never dies.
    /// </summary>
    private IPhysics3D EnsurePhysics3D()
    {
        if (_physics3D is not null) return _physics3D;

        if (PhysicsSettings.Backend == Physics3DBackend.Bepu)
        {
            try
            {
                _physics3D = new Physics.Bepu.BepuPhysics3D();
                return _physics3D;
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to initialize the Bepu physics backend ({0}); falling back to the legacy solver.", ex.Message);
            }
        }

        _physics3D = new LegacyPhysics3D();
        return _physics3D;
    }

    /// <summary>
    /// Casts a ray against this scene's 3D physics and returns the closest hit within
    /// <paramref name="maxDistance"/>. Only meaningful during play mode (when the simulation is live).
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
    {
        if (_physics3D is null)
        {
            hit = default;
            return false;
        }
        return _physics3D.Raycast(this, origin, direction, maxDistance, out hit);
    }

    /// <summary>
    /// Called every frame to render the scene, after the screen is cleared.
    /// </summary>
    public virtual void OnRender()
    {
        System.Numerics.Matrix4x4? viewProjection = null;
        System.Numerics.Vector3 cameraPosition = System.Numerics.Vector3.Zero;
        System.Numerics.Vector4 clearColor = new System.Numerics.Vector4(0.1f, 0.1f, 0.1f, 1.0f);
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

            RenderSystem.Render(this, viewProjection.Value, cameraPosition);
            
            if (is3D)
            {
                Renderer.SetDepthTest(false);
                Renderer.SetFaceCulling(false);
            }
        }
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
        int id = _nextId++;
        _entities.Add(id);

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
        _entities.Clear();
        _pools.Clear();
        _pendingDestroy.Clear();
        _nextId = 1;
        TeardownPhysics();
    }

    /// <summary>
    /// Disposes the runtime physics backend, if one was built. Called by the <see cref="SceneManager"/>
    /// when the scene is exited so the native-free simulation and its buffers are released. Safe to call
    /// when no backend exists; a later <see cref="UpdateRuntime"/> rebuilds one on demand.
    /// </summary>
    internal void TeardownPhysics()
    {
        if (_physics3D is null) return;
        try
        {
            _physics3D.Dispose();
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to dispose the 3D physics backend: {0}", ex.Message);
        }
        _physics3D = null;
        _collisions?.Reset();
    }

    /// <summary>
    /// Returns a handle to the entity with the given id if it is still alive, otherwise
    /// <see langword="null"/>. Convenience for callers that hold a bare id (such as the editor
    /// remapping a selection after a snapshot restore).
    /// </summary>
    internal Entity? EntityById(int? id) =>
        id is int value && _entities.Contains(value) ? new Entity(value, this) : null;

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

        if (_pools.TryGetValue(typeof(ScriptComponent), out Dictionary<int, object>? scriptPool) &&
            scriptPool.TryGetValue(id, out object? value))
        {
            foreach (EntityBehaviour script in ((ScriptComponent)value).Scripts)
            {
                if (script.Started)
                {
                    script.OnDestroy();
                }
            }
        }

        _entities.Remove(id);
        foreach (Dictionary<int, object> pool in _pools.Values)
        {
            pool.Remove(id);
        }
    }

    /// <summary>
    /// Returns every entity that has a component of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The component type to match.</typeparam>
    /// <returns>A snapshot of the matching entities, safe to modify the scene while iterating.</returns>
    public IReadOnlyList<Entity> View<T>()
        where T : class
    {
        var result = new List<Entity>();
        if (_pools.TryGetValue(typeof(T), out Dictionary<int, object>? pool))
        {
            foreach (int id in pool.Keys)
            {
                result.Add(new Entity(id, this));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns every entity that has components of both <typeparamref name="T1"/> and <typeparamref name="T2"/>.
    /// </summary>
    /// <typeparam name="T1">The first component type to match.</typeparam>
    /// <typeparam name="T2">The second component type to match.</typeparam>
    /// <returns>A snapshot of the matching entities, safe to modify the scene while iterating.</returns>
    public IReadOnlyList<Entity> View<T1, T2>()
        where T1 : class
        where T2 : class
    {
        var result = new List<Entity>();
        if (!_pools.TryGetValue(typeof(T1), out Dictionary<int, object>? pool1) ||
            !_pools.TryGetValue(typeof(T2), out Dictionary<int, object>? pool2))
        {
            return result;
        }

        // Iterate the smaller pool and probe the larger one.
        (Dictionary<int, object> smaller, Dictionary<int, object> larger) =
            pool1.Count <= pool2.Count ? (pool1, pool2) : (pool2, pool1);

        foreach (int id in smaller.Keys)
        {
            if (larger.ContainsKey(id))
            {
                result.Add(new Entity(id, this));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns every entity that has a component of type <typeparamref name="T"/> which is both enabled and
    /// on an entity active in the hierarchy — the standard gate a system applies before touching a
    /// component — paired with that component. The result is a snapshot, so it is safe to spawn or destroy
    /// entities while iterating (for example from a script or a custom <see cref="ISystem"/>).
    /// </summary>
    /// <typeparam name="T">The component type to match.</typeparam>
    public IReadOnlyList<(Entity Entity, T Component)> ViewActive<T>()
        where T : Component
    {
        var result = new List<(Entity, T)>();
        foreach (Entity entity in View<T>())
        {
            if (!entity.IsActiveInHierarchy())
            {
                continue;
            }

            T component = GetComponent<T>(entity);
            if (component.Enabled)
            {
                result.Add((entity, component));
            }
        }

        return result;
    }

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
        foreach (int id in _entities)
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

        // Pass 1: re-mint ids and move each entity's component instances into this scene's pools.
        var remap = new Dictionary<int, int>(order.Count);
        foreach (int oldId in order)
        {
            int newId = _nextId++;
            remap[oldId] = newId;
            _entities.Add(newId);

            foreach (Dictionary<int, object> sourcePool in source._pools.Values)
            {
                if (sourcePool.Remove(oldId, out object? component))
                {
                    PoolFor(component.GetType())[newId] = component;
                }
            }

            source._entities.Remove(oldId);
        }

        // Pass 2: rebind every stored entity handle now that all new ids exist.
        foreach (int newId in remap.Values)
        {
            var entity = new Entity(newId, this);

            if (TryGetComponent(entity, out TransformComponent? transform))
            {
                transform.Entity = entity;
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

    internal bool IsAlive(Entity entity) => _entities.Contains(entity.Id);

    internal T AddComponent<T>(Entity entity, T component)
        where T : class
    {
        if (component is TransformComponent transform)
        {
            transform.Entity = entity;
        }

        PoolFor(typeof(T))[entity.Id] = component;
        return component;
    }

    internal T GetComponent<T>(Entity entity)
        where T : class
    {
        if (TryGetComponent(entity, out T? component))
        {
            return component;
        }

        throw new InvalidOperationException($"Entity does not have a component of type {typeof(T).Name}.");
    }

    internal bool TryGetComponent<T>(Entity entity, [NotNullWhen(true)] out T? component)
        where T : class
    {
        if (_pools.TryGetValue(typeof(T), out Dictionary<int, object>? pool) &&
            pool.TryGetValue(entity.Id, out object? value))
        {
            component = (T)value;
            return true;
        }

        component = null;
        return false;
    }

    internal bool HasComponent<T>(Entity entity)
        where T : class =>
        _pools.TryGetValue(typeof(T), out Dictionary<int, object>? pool) && pool.ContainsKey(entity.Id);

    internal void RemoveComponent<T>(Entity entity)
        where T : class
    {
        if (_pools.TryGetValue(typeof(T), out Dictionary<int, object>? pool))
        {
            pool.Remove(entity.Id);
        }
    }

    // Non-generic component access, keyed by runtime type. These mirror the generic API for callers
    // that only know a component's Type at runtime (e.g. the editor's reflection-based inspector).
    // Components are always stored under their concrete type, so keying by Type/GetType() is consistent
    // with the generic path.

    internal bool HasComponent(Entity entity, Type type) =>
        _pools.TryGetValue(type, out Dictionary<int, object>? pool) && pool.ContainsKey(entity.Id);

    internal object? GetComponent(Entity entity, Type type) =>
        _pools.TryGetValue(type, out Dictionary<int, object>? pool) && pool.TryGetValue(entity.Id, out object? value)
            ? value
            : null;

    internal bool TryGetComponent(Entity entity, Type type, [NotNullWhen(true)] out object? component)
    {
        if (_pools.TryGetValue(type, out Dictionary<int, object>? pool) && pool.TryGetValue(entity.Id, out component))
        {
            return true;
        }

        component = null;
        return false;
    }

    internal Component AddComponent(Entity entity, Component component)
    {
        if (component is TransformComponent transform)
        {
            transform.Entity = entity;
        }

        PoolFor(component.GetType())[entity.Id] = component;
        return component;
    }

    internal void RemoveComponent(Entity entity, Type type)
    {
        if (_pools.TryGetValue(type, out Dictionary<int, object>? pool))
        {
            pool.Remove(entity.Id);
        }
    }

    private Dictionary<int, object> PoolFor(Type type)
    {
        if (!_pools.TryGetValue(type, out Dictionary<int, object>? pool))
        {
            pool = new Dictionary<int, object>();
            _pools[type] = pool;
        }

        return pool;
    }
}
