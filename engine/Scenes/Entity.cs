using System.Diagnostics.CodeAnalysis;

namespace Spot.Scenes;

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
    /// Gets or sets the entity's name (stored in its <see cref="TagComponent"/>).
    /// </summary>
    public string Name
    {
        get => GetComponent<TagComponent>().Name;
        set => GetComponent<TagComponent>().Name = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether this entity is enabled.
    /// If disabled, it and its children will not be updated or rendered.
    /// </summary>
    public bool Enabled
    {
        get => GetComponent<TagComponent>().Enabled;
        set => GetComponent<TagComponent>().Enabled = value;
    }

    /// <summary>
    /// Checks if this entity and all its ancestors are enabled.
    /// </summary>
    public bool IsActiveInHierarchy()
    {
        if (!Enabled) return false;
        var current = Parent;
        while (current != null)
        {
            if (!current.Value.Enabled) return false;
            current = current.Value.Parent;
        }
        return true;
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
        get => TryGetComponent(out RelationshipComponent? rel) ? rel.Parent : null;
    }

    /// <summary>
    /// Gets the entity's children.
    /// </summary>
    public IEnumerable<Entity> Children
    {
        get => TryGetComponent(out RelationshipComponent? rel) ? rel.Children : Enumerable.Empty<Entity>();
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

        if (!TryGetComponent(out RelationshipComponent? rel))
            rel = AddComponent(new RelationshipComponent());

        if (rel.Parent == parent) return;

        if (rel.Parent != null)
        {
            if (rel.Parent.Value.TryGetComponent(out RelationshipComponent? currentParentRel))
            {
                currentParentRel.Children.Remove(this);
            }
        }

        rel.Parent = parent;

        if (parent != null)
        {
            if (!parent.Value.TryGetComponent(out RelationshipComponent? parentRel))
                parentRel = parent.Value.AddComponent(new RelationshipComponent());
            
            parentRel.Children.Add(this);
        }
    }

    /// <summary>
    /// Attaches a component to the entity, replacing any existing component of the same type.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="component">The component instance.</param>
    /// <returns>The attached component.</returns>
    public T AddComponent<T>(T component)
        where T : class => OwningScene.AddComponent(this, component);

    /// <summary>
    /// Gets the entity's component of the given type.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>The component.</returns>
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
    /// Attaches a new script of the given type to the entity.
    /// </summary>
    /// <typeparam name="T">The script type.</typeparam>
    /// <returns>The attached script.</returns>
    public T AddScript<T>()
        where T : EntityBehaviour, new() => AddScript(new T());

    /// <summary>
    /// Attaches an already-constructed script to the entity (useful when the script needs constructor
    /// arguments).
    /// </summary>
    /// <typeparam name="T">The script type.</typeparam>
    /// <param name="script">The script instance.</param>
    /// <returns>The attached script.</returns>
    public T AddScript<T>(T script)
        where T : EntityBehaviour
    {
        script.Entity = this;

        if (!TryGetComponent(out ScriptComponent? scripts))
        {
            scripts = AddComponent(new ScriptComponent());
        }

        scripts.Scripts.Add(script);
        return script;
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
