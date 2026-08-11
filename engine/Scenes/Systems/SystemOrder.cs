namespace Spot.Scenes;

/// <summary>
/// The execution-order slots of the engine's built-in play-mode systems, spaced so custom
/// <see cref="ISystem"/>s can be ordered before, between, or after them. The default order runs the
/// simulation (character controllers, then 2D and 3D physics), then poses animation, advances particles
/// and audio, and finally runs scripts — so a script's <c>OnUpdate</c> observes the world after physics
/// has resolved it this frame.
/// </summary>
public static class SystemOrder
{
    /// <summary>Kinematic character controllers, resolved before the physics step.</summary>
    public const int CharacterController = 100;

    /// <summary>2D physics integration and collision resolution.</summary>
    public const int Physics2D = 200;

    /// <summary>3D physics step and collision-event dispatch.</summary>
    public const int Physics3D = 300;

    /// <summary>Skeletal animation sampling and bone posing.</summary>
    public const int Animation = 400;

    /// <summary>Particle emitter simulation.</summary>
    public const int Particles = 500;

    /// <summary>Spatial audio: voice positions and the listener.</summary>
    public const int Audio = 600;

    /// <summary>User scripts (<see cref="EntityBehaviour"/>), run last so they see the resolved world.</summary>
    public const int Scripts = 700;
}
