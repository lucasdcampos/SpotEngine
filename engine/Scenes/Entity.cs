using System.Diagnostics.CodeAnalysis;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.Engine;

/// <summary>
/// A lightweight handle to an entity within a <see cref="Scene"/>. Entities are just an identity;
/// their data lives in components stored by the scene.
/// </summary>
public readonly struct Entity : IEquatable<Entity>
{
    private readonly Scene? _scene;

    internal Entity(int id, Scene scene)
    {
        Id = id;
        _scene = scene;
    }

    /// <summary>
    /// Gets the entity's identifier within its scene.
    /// </summary>
    internal int Id { get; }

    /// <summary>
    /// Gets a value indicating whether the entity still exists in its scene.
    /// </summary>
    public bool IsValid => _scene is not null && _scene.IsAlive(this);

    /// <summary>
    /// Gets a value indicating whether this handle refers to a scene at all — <see langword="false"/> for the
    /// default handle a component holds before it is attached. Cheaper than <see cref="IsValid"/>, which also
    /// checks the entity is still alive.
    /// </summary>
    internal bool HasScene => _scene is not null;

    /// <summary>
    /// Gets or sets the entity's name (stored in its <see cref="Label"/>).
    /// </summary>
    public string Name
    {
        get => GetComponent<Label>().Name;
        set => GetComponent<Label>().Name = value;
    }

    /// <summary>
    /// Gets or sets the entity's tag (stored in its <see cref="Label"/>). Tags classify
    /// entities for lookup; see <see cref="Scene.FindByTag"/> and <see cref="CompareTag"/>.
    /// </summary>
    public string Tag
    {
        get => GetComponent<Label>().Tag;
        set => GetComponent<Label>().Tag = value ?? string.Empty;
    }

    /// <summary>
    /// Returns whether the entity's <see cref="Tag"/> equals <paramref name="tag"/> (ordinal comparison).
    /// </summary>
    /// <param name="tag">The tag to compare against.</param>
    public bool CompareTag(string tag) => string.Equals(Tag, tag, StringComparison.Ordinal);

    /// <summary>
    /// Gets or sets a value indicating whether this entity is enabled.
    /// If disabled, it and its children will not be updated or rendered.
    /// </summary>
    public bool Enabled
    {
        get => GetComponent<Label>().Enabled;
        set => GetComponent<Label>().Enabled = value;
    }

    /// <summary>
    /// Checks if this entity and all its ancestors are enabled.
    /// </summary>
    public bool IsActiveInHierarchy()
    {
        return OwningScene.IsActiveInHierarchy(Id);
    }

    /// <summary>
    /// Gets or sets the entity's stable, serialization-time identifier (stored in its
    /// <see cref="Label"/>). Used to reference the entity from serialized data such as an
    /// <see cref="Entity"/>-typed script field. Empty until the entity is first serialized; use
    /// <see cref="EnsurePersistentId"/> to allocate one.
    /// </summary>
    internal string PersistentId
    {
        get => GetComponent<Label>().EntityGuid;
        set => GetComponent<Label>().EntityGuid = value ?? string.Empty;
    }

    /// <summary>
    /// Returns the entity's stable <see cref="PersistentId"/>, allocating a fresh one the first time it is
    /// needed so entity references have a target to point at.
    /// </summary>
    internal string EnsurePersistentId()
    {
        Label label = GetComponent<Label>();
        if (string.IsNullOrEmpty(label.EntityGuid))
        {
            label.EntityGuid = System.Guid.NewGuid().ToString("N");
        }

        return label.EntityGuid;
    }

    /// <summary>
    /// Gets the scene this entity belongs to.
    /// </summary>
    public Scene Scene => OwningScene;

    private Scene OwningScene =>
        _scene ?? throw new InvalidOperationException("This entity is not associated with a scene.");

    /// <summary>
    /// Gets the entity's parent, if any.
    /// </summary>
    public Entity? Parent
    {
        get => TryGetComponent(out Relationship? rel) ? rel.Parent : null;
    }

    /// <summary>
    /// Gets the entity's children.
    /// </summary>
    public IEnumerable<Entity> Children
    {
        get => TryGetComponent(out Relationship? rel) ? rel.Children : Enumerable.Empty<Entity>();
    }

    /// <summary>
    /// Checks if this entity is a descendant of the given entity.
    /// </summary>
    public bool IsDescendantOf(Entity entity)
    {
        Entity? current = this.Parent;
        while (current != null)
        {
            if (current.Value == entity) return true;
            current = current.Value.Parent;
        }
        return false;
    }

    /// <summary>
    /// Sets the entity's parent.
    /// </summary>
    /// <param name="parent">The new parent entity.</param>
    public void SetParent(Entity? parent)
    {
        if (parent != null && (parent.Value == this || parent.Value.IsDescendantOf(this)))
        {
            return; // Prevent circular hierarchy
        }

        if (!TryGetComponent(out Relationship? rel))
            rel = AddComponent(new Relationship());

        if (rel.Parent == parent) return;

        if (rel.Parent != null)
        {
            if (rel.Parent.Value.TryGetComponent(out Relationship? currentParentRel))
            {
                currentParentRel.Children.Remove(this);
            }
        }

        rel.Parent = parent;

        if (parent != null)
        {
            if (!parent.Value.TryGetComponent(out Relationship? parentRel))
                parentRel = parent.Value.AddComponent(new Relationship());

            parentRel.Children.Add(this);
        }

        // Reparenting changes this subtree's active-in-hierarchy chain; drop the scene's memoized results.
        OwningScene.InvalidateHierarchyActive();
    }

    /// <summary>
    /// Attaches a component to the entity, replacing any existing component of the same type.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="component">The component instance.</param>
    /// <returns>The attached component.</returns>
    public T AddComponent<T>(T component)
        where T : Component => OwningScene.AddComponent(this, component);

    /// <summary>
    /// Attaches a new component of the given type to the entity, replacing any existing component of the same
    /// type. This is how a game's own components are attached in code:
    /// <c>entity.AddComponent&lt;PlayerMovement&gt;()</c>.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>The attached component.</returns>
    public T AddComponent<T>()
        where T : Component, new() => OwningScene.AddComponent(this, new T());

    /// <summary>
    /// Returns every component on this entity assignable to <typeparamref name="T"/> (a concrete type, a base
    /// class or an interface), in the order they were added.
    /// </summary>
    /// <typeparam name="T">The component type to match.</typeparam>
    /// <returns>A new list of the matching components.</returns>
    public List<T> GetComponents<T>()
        where T : class => OwningScene.GetComponents<T>(this);

    /// <summary>Gets the entity's components in the order they were added.</summary>
    public IReadOnlyList<Component> Components => OwningScene.ComponentsOf(this);

    /// <summary>
    /// Gets the entity's component of the given type, throwing if it is absent. <typeparamref name="T"/> may be a
    /// concrete component type, a base class or an interface (the first match in the entity's order). Use
    /// <see cref="TryGetComponent{T}(out T)"/> when the component may not be present. (The non-generic
    /// <see cref="GetComponent(Type)"/> returns <see langword="null"/> instead of throwing.)
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>The component.</returns>
    /// <exception cref="InvalidOperationException">The entity has no component of type <typeparamref name="T"/>.</exception>
    public T GetComponent<T>()
        where T : class => OwningScene.GetComponent<T>(this);

    /// <summary>
    /// Tries to get the entity's component of the given type.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="component">The component, if present.</param>
    /// <returns><see langword="true"/> if the component was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetComponent<T>([NotNullWhen(true)] out T? component)
        where T : class => OwningScene.TryGetComponent(this, out component);

    /// <summary>
    /// Determines whether the entity has a component of the given type.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns><see langword="true"/> if the component is present; otherwise, <see langword="false"/>.</returns>
    public bool HasComponent<T>()
        where T : class => OwningScene.HasComponent<T>(this);

    /// <summary>
    /// Removes the entity's component of the given type, if present.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    public void RemoveComponent<T>()
        where T : class => OwningScene.RemoveComponent<T>(this);

    /// <summary>
    /// Returns the first component of type <typeparamref name="T"/> found on this entity or, failing
    /// that, anywhere in its descendants (depth-first). Returns <see langword="null"/> if none exists.
    /// </summary>
    /// <typeparam name="T">The component type to search for.</typeparam>
    public T? GetComponentInChildren<T>()
        where T : class
    {
        if (TryGetComponent(out T? component))
        {
            return component;
        }

        foreach (Entity child in Children)
        {
            T? found = child.GetComponentInChildren<T>();
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the first component of type <typeparamref name="T"/> found on this entity or, failing
    /// that, walking up its ancestors. Returns <see langword="null"/> if none exists.
    /// </summary>
    /// <typeparam name="T">The component type to search for.</typeparam>
    public T? GetComponentInParent<T>()
        where T : class
    {
        Entity? current = this;
        while (current is Entity entity)
        {
            if (entity.TryGetComponent(out T? component))
            {
                return component;
            }

            current = entity.Parent;
        }

        return null;
    }

    /// <summary>
    /// Determines whether the entity has a component of the given runtime type.
    /// </summary>
    /// <param name="type">The component type.</param>
    /// <returns><see langword="true"/> if the component is present; otherwise, <see langword="false"/>.</returns>
    public bool HasComponent(Type type) => OwningScene.HasComponent(this, type);

    /// <summary>
    /// Gets the entity's component of the given runtime type, or <see langword="null"/> if absent. The
    /// runtime-<see cref="Type"/> overloads mirror the generic ones for callers that only know the type at
    /// runtime (such as a reflection-based inspector); note this returns <see langword="null"/> when absent,
    /// whereas the generic <see cref="GetComponent{T}()"/> throws.
    /// </summary>
    /// <param name="type">The component type.</param>
    /// <returns>The component, or <see langword="null"/>.</returns>
    public object? GetComponent(Type type) => OwningScene.GetComponent(this, type);

    /// <summary>
    /// Tries to get the entity's component of the given runtime type — the non-generic counterpart to
    /// <see cref="TryGetComponent{T}(out T)"/>.
    /// </summary>
    /// <param name="type">The component type.</param>
    /// <param name="component">The component, if present.</param>
    /// <returns><see langword="true"/> if the component was found; otherwise <see langword="false"/>.</returns>
    public bool TryGetComponent(Type type, [NotNullWhen(true)] out object? component) =>
        OwningScene.TryGetComponent(this, type, out component);

    /// <summary>
    /// Attaches a component to the entity, replacing any existing component of the same runtime type.
    /// </summary>
    /// <param name="component">The component instance.</param>
    /// <returns>The attached component.</returns>
    public Component AddComponent(Component component) => OwningScene.AddComponent(this, component);

    /// <summary>
    /// Removes the entity's component of the given runtime type, if present.
    /// </summary>
    /// <param name="type">The component type.</param>
    public void RemoveComponent(Type type) => OwningScene.RemoveComponent(this, type);

    /// <summary>
    /// Marks this entity to survive scene switches instead of being destroyed with its scene (the
    /// engine's <c>DontDestroyOnLoad</c>). The whole subtree carries over with live component and script
    /// state preserved, so it must be a root: an entity with a parent is detached to the scene root
    /// first, matching Unity's behaviour.
    /// </summary>
    public void DontDestroyOnLoad()
    {
        if (Parent != null)
        {
            SetParent(null);
        }

        GetComponent<Label>().Persistent = true;
    }

    /// <inheritdoc />
    public bool Equals(Entity other) => Id == other.Id && ReferenceEquals(_scene, other._scene);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, _scene);

    /// <summary>Compares two entities for equality.</summary>
    public static bool operator ==(Entity left, Entity right) => left.Equals(right);

    /// <summary>Compares two entities for inequality.</summary>
    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
}
