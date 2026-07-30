using System.Diagnostics.CodeAnalysis;
using Spot.Core;
using Spot.Events;
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

    /// <summary>
    /// Called once when the scene becomes active. Create resources and entities here.
    /// </summary>
    public virtual void OnEnter()
    {
        var window = Spot.Core.Application.Instance.Window;
        foreach (var entity in View<CameraComponent>())
        {
            var cc = GetComponent<CameraComponent>(entity);
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
        Spot.Physics.Physics2DSystem.Update(this, deltaTime);
        ScriptSystem.Update(this, deltaTime);
        FlushDestroyed();
    }

    /// <summary>
    /// Called every frame to render the scene, after the screen is cleared.
    /// </summary>
    public virtual void OnRender()
    {
        System.Numerics.Matrix4x4? viewProjection = null;
        System.Numerics.Vector4 clearColor = new System.Numerics.Vector4(0.1f, 0.1f, 0.1f, 1.0f);
        bool is3D = false;
        
        foreach (var entity in View<CameraComponent>())
        {
            var cc = entity.GetComponent<CameraComponent>();
            if (cc.Primary)
            {
                if (HasComponent<Transform>(entity))
                {
                    var transform = GetComponent<Transform>(entity);
                    viewProjection = cc.GetViewProjection(transform);
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
                Renderer.SetDepthTest(true);
                
            RenderSystem.Render(this, viewProjection.Value);
            
            if (is3D)
                Renderer.SetDepthTest(false);
        }
    }

    /// <summary>
    /// Called every frame to build the scene's ImGui user interface.
    /// </summary>
    public virtual void OnImGuiRender()
    {
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
            var cc = GetComponent<CameraComponent>(entity);
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
    /// Creates a new entity with a <see cref="TagComponent"/> and a <see cref="Transform"/>. Safe to
    /// call at any time, including from a script.
    /// </summary>
    /// <param name="name">The entity name.</param>
    /// <returns>The new entity.</returns>
    public Entity Instantiate(string name = "Entity")
    {
        int id = _nextId++;
        _entities.Add(id);

        var entity = new Entity(id, this);
        entity.AddComponent(new TagComponent(name));
        entity.AddComponent(new RelationshipComponent());
        entity.AddComponent(new Transform());
        return entity;
    }

    /// <summary>
    /// Marks an entity for destruction. The entity and its components are removed at the end of the
    /// current frame, so it is safe to call from a script (even on the entity running the script).
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    public void Destroy(Entity entity) => _pendingDestroy.Add(entity.Id);

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

    internal bool IsAlive(Entity entity) => _entities.Contains(entity.Id);

    internal T AddComponent<T>(Entity entity, T component)
        where T : class
    {
        if (component is Transform transform)
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
