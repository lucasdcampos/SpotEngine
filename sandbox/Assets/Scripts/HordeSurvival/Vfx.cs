using System.Numerics;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Game;

/// <summary>
/// Fire-and-forget particle bursts for the Horde Survival demo. Each call spawns a short-lived,
/// self-destructing emitter (a flat-2D <see cref="ParticleSystemComponent"/> using the engine's built-in
/// round dot) at a world position and emits a one-shot puff of glowing particles. Used for enemy deaths,
/// muzzle flashes, spawn-in poofs, bullet impacts and player hits.
/// </summary>
public static class Vfx
{
    /// <summary>Emits a one-shot burst of particles at <paramref name="position"/>.</summary>
    /// <param name="scene">The scene to spawn the emitter in.</param>
    /// <param name="position">The world-space center of the burst.</param>
    /// <param name="color">The particle color (RGBA).</param>
    /// <param name="count">How many particles to emit.</param>
    /// <param name="speed">Initial outward speed.</param>
    /// <param name="size">Starting particle size (shrinks to nothing over its life).</param>
    /// <param name="lifetime">How long each particle lives, in seconds.</param>
    /// <param name="additive">When true, particles glow (additive blending); otherwise normal alpha.</param>
    public static void Burst(
        Scene scene,
        Vector2 position,
        Vector4 color,
        int count,
        float speed,
        float size,
        float lifetime,
        bool additive = true)
    {
        Entity emitter = scene.Instantiate("VFX");
        emitter.GetComponent<TransformComponent>().Position = new Vector3(position, 0.2f);

        var particles = emitter.AddComponent(new ParticleSystemComponent
        {
            PlayOnAwake = false,
            Looping = false,
            EmissionRate = 0.0f,
            MaxParticles = count,
            Shape = ParticleEmitterShape.Sphere,
            Radius = size * 0.25f,
            Space = ParticleSimulationSpace.Local,
            RenderMode = ParticleRenderMode.Flat2D,
            Blend = additive ? ParticleBlend.Additive : ParticleBlend.Alpha,
            StartLifetime = lifetime,
            StartSpeed = speed,
            StartSize = size,
            EndSize = 0.0f,
            StartColor = color,
            EndColor = new Vector4(color.X, color.Y, color.Z, 0.0f),
            Damping = 3.0f,
            Randomness = 0.6f,
        });

        particles.Emit(count);
        emitter.AddScript<AutoDestruct>().Lifetime = lifetime + 0.3f;
    }
}
