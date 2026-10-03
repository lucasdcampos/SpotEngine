using System.Diagnostics.CodeAnalysis;

namespace Spot.Engine.Scenes;

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
    private readonly Dictionary<Type, Dictionary<int, Component>> _pools = new();

    // Each entity's components in the order they were added: the order hooks run in, the inspector shows them
    // in, and polymorphic lookups scan. Mirrors the pools, which stay the fast exact-type index.
    private readonly Dictionary<int, List<Component>> _ordered = new();

    // Every user component across the scene, in the order they were added, plus a snapshot the component
    // system iterates (rebuilt only after a change, so a steady frame allocates nothing).
    private readonly List<Component> _userComponents = new();
    private Component[]? _userSnapshot;
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
        foreach (Dictionary<int, Component> pool in _pools.Values)
        {
            pool.Remove(id);
        }

        if (_ordered.Remove(id, out List<Component>? ordered))
        {
            foreach (Component component in ordered)
            {
                if (component.IsUserComponent)
                {
                    _userComponents.Remove(component);
                }
            }
        }

        InvalidateCaches();
    }

    /// <summary>Resets the store to empty and restarts id allocation. Used when re-hydrating a scene in place.</summary>
    public void Clear()
    {
        _entities.Clear();
        _pools.Clear();
        _ordered.Clear();
        _userComponents.Clear();
        _userSnapshot = null;
        _viewCache.Clear();
        _viewCache2.Clear();
        _hierarchy.Invalidate();
        _nextId = 1;
    }

    // --- components ---

    /// <summary>
    /// Stores <paramref name="component"/> for <paramref name="id"/> under its runtime type, replacing any
    /// component of that exact type. A replaced component keeps its slot in the entity's order.
    /// </summary>
    /// <returns>The component that was replaced, or <see langword="null"/>.</returns>
    public Component? Set(int id, Component component)
    {
        Dictionary<int, Component> pool = PoolFor(component.GetType());
        List<Component> ordered = OrderedFor(id);
        pool.TryGetValue(id, out Component? replaced);
        pool[id] = component;

        int slot = replaced is null ? -1 : ordered.IndexOf(replaced);
        if (slot >= 0)
        {
            ordered[slot] = component;
        }
        else
        {
            ordered.Add(component);
        }

        if (replaced is not null && replaced.IsUserComponent)
        {
            _userComponents.Remove(replaced);
        }

        if (component.IsUserComponent)
        {
            _userComponents.Add(component);
        }

        InvalidateCaches();
        return replaced;
    }

    public T Get<T>(int id)
        where T : class =>
        TryGet(id, out T? component)
            ? component
            : throw new InvalidOperationException($"Entity does not have a component of type {typeof(T).Name}.");

    /// <summary>
    /// Finds the entity's component of type <typeparamref name="T"/>: the exact type first (one dictionary
    /// probe), then — unless <typeparamref name="T"/> is sealed and so cannot match anything else — the first
    /// component in the entity's order assignable to it, so base classes and interfaces resolve too.
    /// </summary>
    public bool TryGet<T>(int id, [NotNullWhen(true)] out T? component)
        where T : class
    {
        if (TryGet(typeof(T), id, out Component? found))
        {
            component = (T)(object)found;
            return true;
        }

        component = null;
        return false;
    }

    public bool Has<T>(int id)
        where T : class =>
        TryGet(typeof(T), id, out _);

    public Component? Remove<T>(int id)
        where T : class =>
        Remove(typeof(T), id);

    public bool Has(Type type, int id) => TryGet(type, id, out _);

    public Component? Get(Type type, int id) => TryGet(type, id, out Component? component) ? component : null;

    /// <summary>The non-generic counterpart to <see cref="TryGet{T}(int, out T)"/>, with the same lookup rules.</summary>
    public bool TryGet(Type type, int id, [NotNullWhen(true)] out Component? component)
    {
        if (_pools.TryGetValue(type, out Dictionary<int, Component>? pool) && pool.TryGetValue(id, out component))
        {
            return true;
        }

        if (!type.IsSealed && _ordered.TryGetValue(id, out List<Component>? ordered))
        {
            foreach (Component candidate in ordered)
            {
                if (type.IsInstanceOfType(candidate))
                {
                    component = candidate;
                    return true;
                }
            }
        }

        component = null;
        return false;
    }

    /// <summary>
    /// Removes the entity's component of <paramref name="type"/>, resolved the way
    /// <see cref="TryGet(Type, int, out Component)"/> resolves it (so a base class or interface removes the
    /// first match).
    /// </summary>
    /// <returns>The removed component, or <see langword="null"/> when the entity had none.</returns>
    public Component? Remove(Type type, int id)
    {
        if (!TryGet(type, id, out Component? component))
        {
            return null;
        }

        _pools[component.GetType()].Remove(id);
        if (_ordered.TryGetValue(id, out List<Component>? ordered))
        {
            ordered.Remove(component);
        }

        if (component.IsUserComponent)
        {
            _userComponents.Remove(component);
        }

        InvalidateCaches();
        return component;
    }

    /// <summary>The entity's components in the order they were added (empty for an unknown id).</summary>
    public IReadOnlyList<Component> ComponentsOf(int id) =>
        _ordered.TryGetValue(id, out List<Component>? ordered) ? ordered : Array.Empty<Component>();

    /// <summary>
    /// Every user component (see <see cref="Component.IsUserComponent"/>) in the order they were added, as a
    /// snapshot that stays valid while components are added or removed during iteration.
    /// </summary>
    public IReadOnlyList<Component> UserComponents => _userSnapshot ??= _userComponents.ToArray();

    /// <summary>Every component in the store assignable to <typeparamref name="T"/>, grouped by entity.</summary>
    public List<T> All<T>()
        where T : class
    {
        var result = new List<T>();
        if (typeof(T).IsSealed)
        {
            if (_pools.TryGetValue(typeof(T), out Dictionary<int, Component>? pool))
            {
                foreach (Component component in pool.Values)
                {
                    result.Add((T)(object)component);
                }
            }

            return result;
        }

        foreach (List<Component> ordered in _ordered.Values)
        {
            foreach (Component component in ordered)
            {
                if (component is T match)
                {
                    result.Add(match);
                }
            }
        }

        return result;
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
        if (_pools.TryGetValue(typeof(T), out Dictionary<int, Component>? pool))
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
        if (!_pools.TryGetValue(typeof(T1), out Dictionary<int, Component>? pool1) ||
            !_pools.TryGetValue(typeof(T2), out Dictionary<int, Component>? pool2))
        {
            _viewCache2[key] = result;
            return result;
        }

        // Iterate the smaller pool and probe the larger one.
        (Dictionary<int, Component> smaller, Dictionary<int, Component> larger) =
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
        foreach (Dictionary<int, Component> pool in _pools.Values)
        {
            if (pool.Remove(sourceId, out Component? component))
            {
                dest.PoolFor(component.GetType())[destId] = component;
            }
        }

        if (_ordered.Remove(sourceId, out List<Component>? ordered))
        {
            dest._ordered[destId] = ordered;
            foreach (Component component in ordered)
            {
                if (component.IsUserComponent && _userComponents.Remove(component))
                {
                    dest._userComponents.Add(component);
                }
            }
        }

        _entities.Remove(sourceId);
    }

    /// <summary>Clears the derived query and hierarchy-active caches without touching stored state.</summary>
    public void InvalidateCaches()
    {
        _userSnapshot = null;
        _viewCache.Clear();
        _viewCache2.Clear();
        _hierarchy.Invalidate();
    }

    private Dictionary<int, Component> PoolFor(Type type)
    {
        if (!_pools.TryGetValue(type, out Dictionary<int, Component>? pool))
        {
            pool = new Dictionary<int, Component>();
            _pools[type] = pool;
        }

        return pool;
    }

    private List<Component> OrderedFor(int id)
    {
        if (!_ordered.TryGetValue(id, out List<Component>? ordered))
        {
            ordered = new List<Component>();
            _ordered[id] = ordered;
        }

        return ordered;
    }
}
