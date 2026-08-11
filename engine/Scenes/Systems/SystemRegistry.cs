using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// The ordered set of <see cref="ISystem"/>s a <see cref="Scene"/> runs each play-mode frame. Every scene
/// starts with the engine's built-in systems registered (see <see cref="SystemOrder"/>); add or remove
/// systems via <see cref="Scene.RegisterSystem(ISystem)"/> and <see cref="Remove"/> to customize the
/// simulation. Systems execute in ascending <see cref="ISystem.Order"/>, ties keeping registration order.
/// </summary>
public sealed class SystemRegistry
{
    private readonly List<ISystem> _systems = new();
    private readonly HashSet<ISystem> _faultLogged = new();
    private ISystem[]? _ordered;

    /// <summary>Registers a system. Takes effect on the next update.</summary>
    /// <param name="system">The system to add.</param>
    public void Add(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
        _ordered = null;
    }

    /// <summary>Removes a previously registered system.</summary>
    /// <param name="system">The system to remove.</param>
    /// <returns><see langword="true"/> if it was registered; otherwise <see langword="false"/>.</returns>
    public bool Remove(ISystem system)
    {
        _faultLogged.Remove(system);
        _ordered = null;
        return _systems.Remove(system);
    }

    /// <summary>The registered systems in execution order.</summary>
    public IReadOnlyList<ISystem> Ordered => _ordered ??= _systems.OrderBy(static s => s.Order).ToArray();

    /// <summary>
    /// Runs every system in order. Each runs inside its own guard so a system that throws is logged once
    /// and skipped, never taking the frame loop down or stopping the systems after it.
    /// </summary>
    internal void Update(Scene scene, float deltaTime)
    {
        foreach (ISystem system in Ordered)
        {
            try
            {
                system.Update(scene, deltaTime);
            }
            catch (Exception ex)
            {
                if (_faultLogged.Add(system))
                {
                    Log.CoreError("System '{0}' threw during update and will be skipped when it faults. {1}", system.GetType().Name, ex);
                }
            }
        }
    }
}
