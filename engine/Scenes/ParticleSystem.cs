using System.Numerics;
using Spot.Core;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// The play-mode system that advances every <see cref="ParticleSystemComponent"/>: it refreshes each
/// emitter's cached world transform (so world-space spawning and a script's <c>Emit()</c> use the live
/// position) and steps the simulation. Called from <see cref="Scene.UpdateRuntime"/>, so particles only
/// move while a scene is running — never in the editor's edit mode. Each emitter is isolated in its own
/// guard so one faulty emitter can neither crash nor freeze the rest.
/// </summary>
internal static class ParticleSystem
{
    public static void Update(Scene scene, float deltaTime)
    {
        foreach (Entity entity in scene.View<TransformComponent, ParticleSystemComponent>())
        {
            ParticleSystemComponent particles = entity.GetComponent<ParticleSystemComponent>();
            TransformComponent transform = entity.GetComponent<TransformComponent>();
            if (!entity.IsActiveInHierarchy() || !particles.Enabled || !transform.Enabled)
            {
                continue;
            }

            try
            {
                particles.EmitterWorld = transform.Matrix;
                particles.EmitterRotation = RotationOnly(transform.WorldRotation);
                particles.Simulate(deltaTime);
            }
            catch (Exception ex)
            {
                Log.CoreWarn("Particle emitter on entity {0} faulted and was disabled: {1}", entity.Id, ex.Message);
                particles.Enabled = false;
            }
        }
    }

    // A rotation-only matrix (no translation/scale) from a world Euler angle in degrees, used to orient
    // world-space particles' launch velocity by the emitter's facing without scaling their speed.
    private static Matrix4x4 RotationOnly(Vector3 eulerDegrees)
    {
        const float toRad = MathF.PI / 180.0f;
        return Matrix4x4.CreateFromYawPitchRoll(eulerDegrees.Y * toRad, eulerDegrees.X * toRad, eulerDegrees.Z * toRad);
    }
}
