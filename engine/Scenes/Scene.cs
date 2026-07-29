using System.Diagnostics.CodeAnalysis;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// A container of entities and their components. Components are plain data grouped into per-type
/// pools; behavior lives in systems that query the scene (for example a future render system).
/// </summary>
public sealed class Scene
{
    private readonly HashSet<int> _entities = new();
    private readonly Dictionary<Type, Dictionary<int, object>> _pools = new();
    private int _nextId = 1;

    /// <summary>
    /// Creates a new entity with a <see cref="TagComponent"/> and a <see cref="Transform"/>.
    /// </summary>
    /// <param name="name">The entity name.</param>
    /// <returns>The new entity.</returns>
    public Entity CreateEntity(string name = "Entity")
    {
        int id = _nextId++;
        _entities.Add(id);

        var entity = new Entity(id, this);
        entity.AddComponent(new TagComponent(name));
        entity.AddComponent(new Transform());
        return entity;
    }

    /// <summary>
    /// Destroys an entity and all of its components.
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    public void DestroyEntity(Entity entity)
    {
        _entities.Remove(entity.Id);
        foreach (Dictionary<int, object> pool in _pools.Values)
        {
            pool.Remove(entity.Id);
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
