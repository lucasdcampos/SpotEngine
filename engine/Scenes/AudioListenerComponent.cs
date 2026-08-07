namespace Spot.Scenes;

/// <summary>
/// Marks the entity whose transform is the "ears" of the scene: spatial sounds are panned and attenuated
/// relative to it. Typically added to the camera. If several are present, <c>AudioSystem</c> uses the first
/// active one. The listener faces its local +Z, matching the engine's directional convention.
/// </summary>
[ComponentMenu("Audio Listener", Order = 61)]
[SceneComponent("AudioListener")]
public sealed class AudioListenerComponent : Component
{
}
