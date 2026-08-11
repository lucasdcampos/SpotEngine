namespace Spot.Scenes;

/// <summary>
/// A unit of per-frame play-mode logic that runs over a scene's components. Systems are registered on a
/// scene's <see cref="Scene.Systems"/> registry and executed each play tick, in ascending
/// <see cref="Order"/>, from <see cref="Scene.UpdateRuntime"/>.
/// </summary>
/// <remarks>
/// The engine registers its built-in systems (physics, animation, particles, audio, scripts) by default;
/// see <see cref="SystemOrder"/> for the slots they occupy. Implement this interface and register it via
/// <see cref="Scene.RegisterSystem(ISystem)"/> to add custom simulation, choosing an <see cref="Order"/>
/// that places it relative to the built-ins. A system that throws is caught, logged once, and skipped so a
/// faulty system never takes the frame loop down.
/// </remarks>
public interface ISystem
{
    /// <summary>
    /// The execution order of this system; lower runs first. Ties keep registration order. Built-in
    /// systems use the slots on <see cref="SystemOrder"/>; custom systems can sit before, between, or
    /// after them by choosing a value accordingly. Defaults to <c>0</c> (before every built-in).
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Advances this system by one play-mode frame.
    /// </summary>
    /// <param name="scene">The scene being updated.</param>
    /// <param name="deltaTime">The elapsed time in seconds since the previous frame.</param>
    void Update(Scene scene, float deltaTime);
}
