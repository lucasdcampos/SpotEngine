using System.Diagnostics.CodeAnalysis;

namespace Spot.Scenes;

/// <summary>
/// A scene's entity/component store: the entity id set, the per-type component pools, and the derived
/// query caches. Split out of <see cref="Scene"/> so the ECS storage and querying live in one cohesive
/// unit; <see cref="Scene"/> keeps the public entity API and delegates here.
/// </summary>
/// <remarks>
/// Every structural mutation (a component add/remove, an entity removal) clears the cached <c>View</c>
/// results and the hierarchy-active memo together, since both are derived from the same pool contents.
/// The hierarchy memo is owned by a <see cref="HierarchyCache"/> this registry composes and drives.
/// </remarks>
internal sealed class EntityRegistry
{
    private readonly Scene _scene;
    private readonly HashSet<int> _entities = new();
    private readonly Dictionary<Type, Dictionary<int, object>> _pools = new();
    private readonly Dictionary<Type, IReadOnlyList<Entity>> _viewCache = new();
    private readonly Dictionary<(Type, Type), IReadOnlyList<Entity>> _viewCache2 = new();
    private readonly HierarchyCache _hierarchy;
    private int _nextId = 1;

    public EntityRegistry(Scene scene)
    {
        _scene = scene;
        _hierarchy = new HierarchyCache(this);
    }

    // --- entities ---

    public bool Contains(int id) => _entities.Contains(id);

    public IReadOnlyCollection<int> EntityIds => _entities;

    /// <summary>Mints a fresh entity id and registers it (with no components yet).</summary>
    public int CreateEntity()
    {
        int id = _nextId++;
        _entities.Add(id);
        return id;
    }

    /// <summary>Removes an entity and all of its components, then invalidates the derived caches.</summary>
    public void RemoveEntity(int id)
    {
        _entities.Remove(id);
        foreach (Dictionary<int, object> pool in _pools.Values)
        {
            pool.Remove(id);
        }
        InvalidateCaches();
    }

    /// <summary>Resets the store to empty and restarts id allocation. Used when re-hydrating a scene in place.</summary>
    public void Clear()
    {
        _entities.Clear();
        _pools.Clear();
        _viewCache.Clear();
        _viewCache2.Clear();
        _hierarchy.Invalidate();
        _nextId = 1;
    }

    // --- components ---

    /// <summary>Stores <paramref name="component"/> for <paramref name="id"/> under <paramref name="type"/>.</summary>
    public void Set(Type type, int id, object component)
    {
        PoolFor(type)[id] = component;
        InvalidateCaches();
    }

    public T Get<T>(int id)
        where T : class =>
        TryGet(id, out T? component)
            ? component
            : throw new InvalidOperationException($"Entity does not have a component of type {typeof(T).Name}.");

    public bool TryGet<T>(int id, [NotNullWhen(true)] out T? component)
        where T : class
    {
        if (_pools.TryGetValue(typeof(T), out Dictionary<int, object>? pool) && pool.TryGetValue(id, out object? value))
        {
            component = (T)value;
            return true;
        }

        component = null;
        return false;
    }

    public bool Has<T>(int id)
        where T : class =>
        _pools.TryGetValue(typeof(T), out Dictionary<int, object>? pool) && pool.ContainsKey(id);

    public void Remove<T>(int id)
        where T : class =>
        Remove(typeof(T), id);

    public bool Has(Type type, int id) =>
        _pools.TryGetValue(type, out Dictionary<int, object>? pool) && pool.ContainsKey(id);

    public object? Get(Type type, int id) =>
        _pools.TryGetValue(type, out Dictionary<int, object>? pool) && pool.TryGetValue(id, out object? value)
            ? value
            : null;

    public bool TryGet(Type type, int id, [NotNullWhen(true)] out object? component)
    {
        if (_pools.TryGetValue(type, out Dictionary<int, object>? pool) && pool.TryGetValue(id, out component))
        {
            return true;
        }

        component = null;
        return false;
    }

    public void Remove(Type type, int id)
    {
        if (_pools.TryGetValue(type, out Dictionary<int, object>? pool))
        {
            pool.Remove(id);
            InvalidateCaches();
        }
    }

    // --- queries (cached per frame; invalidated by any structural mutation) ---

    /// <summary>Every entity that has a component of type <typeparamref name="T"/>.</summary>
    public IReadOnlyList<Entity> View<T>()
        where T : class
    {
        if (_viewCache.TryGetValue(typeof(T), out IReadOnlyList<Entity>? cached))
        {
            return cached;
        }

        var result = new List<Entity>();
        if (_pools.TryGetValue(typeof(T), out Dictionary<int, object>? pool))
        {
            foreach (int id in pool.Keys)
            {
                result.Add(new Entity(id, _scene));
            }
        }

        _viewCache[typeof(T)] = result;
        return result;
    }

    /// <summary>Every entity that has components of both <typeparamref name="T1"/> and <typeparamref name="T2"/>.</summary>
    public IReadOnlyList<Entity> View<T1, T2>()
        where T1 : class
        where T2 : class
    {
        var key = (typeof(T1), typeof(T2));
        if (_viewCache2.TryGetValue(key, out IReadOnlyList<Entity>? cached))
        {
            return cached;
        }

        var result = new List<Entity>();
        if (!_pools.TryGetValue(typeof(T1), out Dictionary<int, object>? pool1) ||
            !_pools.TryGetValue(typeof(T2), out Dictionary<int, object>? pool2))
        {
            _viewCache2[key] = result;
            return result;
        }

        // Iterate the smaller pool and probe the larger one.
        (Dictionary<int, object> smaller, Dictionary<int, object> larger) =
            pool1.Count <= pool2.Count ? (pool1, pool2) : (pool2, pool1);

        foreach (int id in smaller.Keys)
        {
            if (larger.ContainsKey(id))
            {
                result.Add(new Entity(id, _scene));
            }
        }

        _viewCache2[key] = result;
        return result;
    }

    /// <summary>
    /// Every entity with an enabled component of type <typeparamref name="T"/> on an entity active in the
    /// hierarchy, paired with that component. The standard gate a system applies before touching a component.
    /// </summary>
    public IReadOnlyList<(Entity Entity, T Component)> ViewActive<T>()
        where T : Component
    {
        var result = new List<(Entity, T)>();
        foreach (Entity entity in View<T>())
        {
            if (!IsActiveInHierarchy(entity.Id))
            {
                continue;
            }

            T component = Get<T>(entity.Id);
            if (component.Enabled)
            {
                result.Add((entity, component));
            }
        }

        return result;
    }

    // --- hierarchy-active memo (owned by HierarchyCache, driven from here) ---

    public bool IsActiveInHierarchy(int id) => _hierarchy.IsActiveInHierarchy(id);

    public void InvalidateHierarchyActive() => _hierarchy.Invalidate();

    // --- scene migration ---

    /// <summary>
    /// Moves every component of <paramref name="sourceId"/> out of this registry into <paramref name="dest"/>
    /// under <paramref name="destId"/> and drops the source id from this registry's entity set. Caches are
    /// left untouched; the caller invalidates both registries once the whole subtree has moved.
    /// </summary>
    public void MoveEntityComponentsTo(int sourceId, EntityRegistry dest, int destId)
    {
        foreach (Dictionary<int, object> pool in _pools.Values)
        {
            if (pool.Remove(sourceId, out object? component))
            {
                dest.PoolFor(component.GetType())[destId] = component;
            }
        }

        _entities.Remove(sourceId);
    }

    /// <summary>Clears the derived query and hierarchy-active caches without touching stored state.</summary>
    public void InvalidateCaches()
    {
        _viewCache.Clear();
        _viewCache2.Clear();
        _hierarchy.Invalidate();
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
