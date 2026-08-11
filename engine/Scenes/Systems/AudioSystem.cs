using System.Numerics;
using Spot.Audio;
using Spot.Core;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// The play-mode system that connects scene data to the audio mixer: it points the listener at the active
/// <see cref="AudioListenerComponent"/>, triggers <see cref="AudioSourceComponent.PlayOnAwake"/> once, and
/// keeps each spatial voice positioned at its entity. It is called from <see cref="Scene.UpdateRuntime"/>,
/// so audio only plays while a scene is running — never in the editor's edit mode. Each source is isolated
/// in its own guard so one faulty entity can neither crash nor silence the rest.
/// </summary>
internal static class AudioSystem
{
    public static void Update(Scene scene, float deltaTime)
    {
        _ = deltaTime;

        UpdateListener(scene);

        foreach (Entity entity in scene.View<TransformComponent, AudioSourceComponent>())
        {
            AudioSourceComponent source = entity.GetComponent<AudioSourceComponent>();
            TransformComponent transform = entity.GetComponent<TransformComponent>();
            bool active = entity.IsActiveInHierarchy() && transform.Enabled && source.Enabled;

            source.WorldPosition = transform.WorldPosition;

            try
            {
                if (!source.AwakeHandled)
                {
                    source.AwakeHandled = true;
                    if (active && source.PlayOnAwake && source.Clip is not null)
                    {
                        source.Play();
                    }
                }

                if (source.IsPlaying)
                {
                    // Mute (but don't stop) a source that became inactive, so it resumes seamlessly if
                    // re-enabled, and mirror live inspector/script changes to volume and position.
                    AudioManager.SetVoiceGain(source.CurrentVoice, active ? source.Volume : 0.0f);
                    if (source.Spatial)
                    {
                        AudioManager.SetVoicePosition(source.CurrentVoice, source.WorldPosition);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.CoreWarn("Audio source on entity {0} faulted and was disabled: {1}", entity.Id, ex.Message);
                source.Enabled = false;
            }
        }
    }

    private static void UpdateListener(Scene scene)
    {
        foreach (Entity entity in scene.View<TransformComponent, AudioListenerComponent>())
        {
            if (!entity.IsActiveInHierarchy())
            {
                continue;
            }

            TransformComponent transform = entity.GetComponent<TransformComponent>();
            AudioListenerComponent listener = entity.GetComponent<AudioListenerComponent>();
            if (!transform.Enabled || !listener.Enabled)
            {
                continue;
            }

            Matrix4x4 m = transform.Matrix;
            Vector3 forward = SafeNormalize(new Vector3(m.M31, m.M32, m.M33), Vector3.UnitZ);
            Vector3 up = SafeNormalize(new Vector3(m.M21, m.M22, m.M23), Vector3.UnitY);
            AudioManager.SetListener(transform.WorldPosition, forward, up);
            return; // first active listener wins
        }
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        float length = v.Length();
        return length > 1e-6f ? v / length : fallback;
    }
}
