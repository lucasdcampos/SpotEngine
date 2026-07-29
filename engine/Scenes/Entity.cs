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

    private Scene OwningScene =>
        _scene ?? throw new InvalidOperationException("This entity is not associated with a scene.");

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
