using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// Drives per-entity scripts. Run automatically by the engine for the active scene: each frame it
/// starts newly attached scripts (<see cref="EntityBehaviour.OnCreate"/>) and updates them
/// (<see cref="EntityBehaviour.OnUpdate"/>).
/// </summary>
internal static class ScriptSystem
{
    public static void Update(Scene scene, float deltaTime)
    {
        // Materialize the entities and their scripts first so a script that spawns or destroys
        // entities during its update cannot invalidate the iterator (the classic "collection was
        // modified" crash). Each script runs inside its own guard so one fault neither crashes the
        // engine nor stops the others.
        foreach (Entity entity in scene.View<ScriptComponent>().ToList())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var scriptComp = entity.GetComponent<ScriptComponent>();
            if (!scriptComp.Enabled) continue;

            foreach (EntityBehaviour script in scriptComp.Scripts.ToList())
            {
                if (script.Faulted)
                {
                    continue;
                }

                try
                {
                    if (!script.Started)
                    {
                        script.OnCreate();
                        script.Started = true;
                    }

                    script.OnUpdate(deltaTime);
                }
                catch (Exception ex)
                {
                    Quarantine(script, "update", ex);
                }
            }
        }
    }

    public static void DestroyAll(Scene scene)
    {
        foreach (Entity entity in scene.View<ScriptComponent>().ToList())
        {
            foreach (EntityBehaviour script in entity.GetComponent<ScriptComponent>().Scripts.ToList())
            {
                if (!script.Started)
                {
                    continue;
                }

                // Teardown is still guarded so a throwing OnDestroy cannot abort the shutdown or the
                // cleanup of the remaining scripts.
                try
                {
                    script.OnDestroy();
                }
                catch (Exception ex)
                {
                    Log.CoreError(
                        "Script '{0}' threw from OnDestroy; ignoring. {1}",
                        script.GetType().Name,
                        ex);
                }
            }
        }
    }

    /// <summary>
    /// Disables a script that threw from a lifecycle hook and logs the fault once. The script is
    /// skipped on later frames so the failure is not repeated every frame.
    /// </summary>
    private static void Quarantine(EntityBehaviour script, string phase, Exception ex)
    {
        script.Faulted = true;
        Log.CoreError(
            "Script '{0}' threw during {1} and was disabled. {2}",
            script.GetType().Name,
            phase,
            ex);
    }
}
