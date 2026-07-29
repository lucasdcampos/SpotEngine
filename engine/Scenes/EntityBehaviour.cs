namespace Spot.Scenes;

/// <summary>
/// Base class for per-entity scripts (the counterpart to Unity's MonoBehaviour). Derive from it,
/// override the lifecycle hooks, and attach it with <see cref="Entity.AddScript{T}()"/>. The engine
/// runs it automatically for the active scene.
/// </summary>
public abstract class EntityBehaviour
{
    /// <summary>
    /// Gets the entity this script is attached to.
    /// </summary>
    public Entity Entity { get; internal set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="OnCreate"/> has run.
    /// </summary>
    internal bool Started { get; set; }

    /// <summary>
    /// Gets the attached entity's component of the given type.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>The component.</returns>
    protected T GetComponent<T>()
        where T : class => Entity.GetComponent<T>();

    /// <summary>
    /// Called once, on the first frame after the script is attached.
    /// </summary>
    public virtual void OnCreate()
    {
    }

    /// <summary>
    /// Called every frame while the script's scene is active.
    /// </summary>
    /// <param name="deltaTime">The elapsed time in seconds since the previous frame.</param>
    public virtual void OnUpdate(float deltaTime)
    {
    }

    /// <summary>
    /// Called when the entity is destroyed or its scene is left (only if <see cref="OnCreate"/> ran).
    /// </summary>
    public virtual void OnDestroy()
    {
    }
}
