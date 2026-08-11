namespace Spot.Scenes;

/// <summary>
/// An <see cref="ISystem"/> that runs a delegate each frame, for registering simulation without declaring a
/// dedicated class. The engine uses it to wire its built-in systems; game code can use it too, e.g.
/// <c>scene.RegisterSystem(new DelegateSystem(SystemOrder.Scripts + 1, (s, dt) =&gt; ...))</c>.
/// </summary>
public sealed class DelegateSystem : ISystem
{
    private readonly Action<Scene, float> _update;

    /// <summary>Creates a system that invokes <paramref name="update"/> at the given <paramref name="order"/>.</summary>
    /// <param name="order">The execution order; lower runs first. See <see cref="SystemOrder"/>.</param>
    /// <param name="update">The per-frame callback, receiving the scene and delta time.</param>
    public DelegateSystem(int order, Action<Scene, float> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Order = order;
        _update = update;
    }

    /// <inheritdoc />
    public int Order { get; }

    /// <inheritdoc />
    public void Update(Scene scene, float deltaTime) => _update(scene, deltaTime);
}
