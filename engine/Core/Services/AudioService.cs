using Spot.Audio;

namespace Spot.Core.Services;

/// <summary>
/// Ties the audio subsystem into the application lifecycle: it opens the OpenAL device at startup, applies
/// the global mix every frame, and releases the device on shutdown. Like every service it is resilient —
/// <see cref="AudioManager"/> degrades to silence rather than throwing when no audio device is present.
/// </summary>
public class AudioService : IEngineService
{
    public void Init(Application app) => AudioManager.Init();

    public void Update(float deltaTime) => AudioManager.Update(deltaTime);

    public void Shutdown() => AudioManager.Shutdown();
}
