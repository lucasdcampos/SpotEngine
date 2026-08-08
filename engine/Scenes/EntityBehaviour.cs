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
    /// Gets or sets a value indicating whether the script threw from a lifecycle hook and was
    /// disabled by the engine. A faulted script is skipped on subsequent frames so one broken script
    /// neither crashes the engine nor spams the log, while other scripts keep running.
    /// </summary>
    internal bool Faulted { get; set; }

    /// <summary>
    /// Gets the scene the script's entity belongs to.
    /// </summary>
    protected Scene Scene => Entity.Scene;

    /// <summary>
    /// Gets the attached entity's component of the given type.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>The component.</returns>
    protected T GetComponent<T>()
        where T : class => Entity.GetComponent<T>();

    /// <summary>
    /// Creates a new entity in this script's scene.
    /// </summary>
    /// <param name="name">The entity name.</param>
    /// <returns>The new entity.</returns>
    protected Entity Instantiate(string name = "Entity") => Scene.Instantiate(name);

    /// <summary>
    /// Finds the first entity in this script's scene with the given name, or <see langword="null"/>.
    /// </summary>
    /// <param name="name">The entity name to search for.</param>
    protected Entity? Find(string name) => Scene.Find(name);

    /// <summary>
    /// Finds the first entity in this script's scene tagged <paramref name="tag"/>, or <see langword="null"/>.
    /// </summary>
    /// <param name="tag">The tag to search for.</param>
    protected Entity? FindByTag(string tag) => Scene.FindByTag(tag);

    /// <summary>
    /// Marks the given entity for destruction at the end of the frame.
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    protected void Destroy(Entity entity) => Scene.Destroy(entity);

    /// <summary>
    /// Marks this script's own entity for destruction at the end of the frame.
    /// </summary>
    protected void Destroy() => Scene.Destroy(Entity);

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

    /// <summary>
    /// Called every frame to render UI with ImGui.
    /// </summary>
    public virtual void OnImGuiRender()
    {
    }

    /// <summary>
    /// Called on the physics step when this entity's (non-trigger) collider begins touching another.
    /// </summary>
    /// <param name="collision">The contact, including the other entity and a contact normal/point.</param>
    public virtual void OnCollisionEnter(Physics.Collision collision)
    {
    }

    /// <summary>
    /// Called on each physics step while this entity's collider keeps touching another.
    /// </summary>
    /// <param name="collision">The contact, including the other entity and a contact normal/point.</param>
    public virtual void OnCollisionStay(Physics.Collision collision)
    {
    }

    /// <summary>
    /// Called on the physics step when this entity's collider stops touching another.
    /// </summary>
    /// <param name="collision">The contact that ended (the other entity; normal/point are the last known values).</param>
    public virtual void OnCollisionExit(Physics.Collision collision)
    {
    }

    /// <summary>
    /// Called on the physics step when another collider enters this entity's trigger volume (or this
    /// entity's collider enters another's trigger). Triggers overlap without a physical response.
    /// </summary>
    /// <param name="other">The other entity in the overlap.</param>
    public virtual void OnTriggerEnter(Entity other)
    {
    }

    /// <summary>
    /// Called on each physics step while another collider stays within this entity's trigger overlap.
    /// </summary>
    /// <param name="other">The other entity in the overlap.</param>
    public virtual void OnTriggerStay(Entity other)
    {
    }

    /// <summary>
    /// Called on the physics step when a collider leaves this entity's trigger overlap.
    /// </summary>
    /// <param name="other">The other entity that left the overlap.</param>
    public virtual void OnTriggerExit(Entity other)
    {
    }
}
