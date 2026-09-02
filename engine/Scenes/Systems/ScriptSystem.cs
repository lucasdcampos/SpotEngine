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
        // First pass — start, enable/disable and update. View (not ViewActive) so disabled scripts are still
        // observed and can fire OnDisable on the frame they go inactive. The list is snapshotted so a script
        // that spawns or destroys entities during its update cannot invalidate the iterator (the classic
        // "collection was modified" crash). Each script runs inside its own guard so one fault neither crashes
        // the engine nor stops the others.
        foreach (Entity entity in scene.View<ScriptComponent>())
        {
            ScriptComponent scriptComp = entity.GetComponent<ScriptComponent>();
            bool active = entity.IsActiveInHierarchy() && scriptComp.Enabled;

            foreach (EntityBehaviour script in scriptComp.Scripts.ToList())
            {
                if (script.Faulted)
                {
                    continue;
                }

                try
                {
                    if (active)
                    {
                        if (!script.Started)
                        {
                            script.OnCreate();
                            script.Started = true;
                        }

                        // Enable edge: fires on first activation (after OnCreate) and every re-enable after.
                        if (!script.ActiveLastFrame)
                        {
                            script.OnEnable();
                            script.ActiveLastFrame = true;
                        }

                        script.OnUpdate(deltaTime);

                        // Advance coroutines/invokes after the script's own update, sharing its guard so a
                        // scheduling fault is quarantined the same way. Unscaled delta drives realtime waits.
                        script.TickScheduling(deltaTime, Time.UnscaledDeltaTime);
                    }
                    else if (script.ActiveLastFrame)
                    {
                        // Disable edge: the entity or its script component was turned off this frame.
                        script.ActiveLastFrame = false;
                        script.OnDisable();
                    }
                }
                catch (Exception ex)
                {
                    Quarantine(script, "update", ex);
                }
            }
        }

        // Second pass — late update, after every script's OnUpdate has run this frame so it observes their
        // changes (a follow camera sees its target's moved position). ActiveLastFrame gates on a script that
        // has been started and had its OnEnable run this frame or earlier.
        foreach ((Entity _, ScriptComponent scriptComp) in scene.ViewActive<ScriptComponent>())
        {
            foreach (EntityBehaviour script in scriptComp.Scripts.ToList())
            {
                if (script.Faulted || !script.ActiveLastFrame) continue;

                try
                {
                    script.OnLateUpdate(deltaTime);
                }
                catch (Exception ex)
                {
                    Quarantine(script, "late_update", ex);
                }
            }
        }
    }

    /// <summary>
    /// Runs <see cref="EntityBehaviour.OnFixedUpdate"/> for every started, active script. Registered as a
    /// system just before the physics step so scripts can apply forces or move bodies in step with the solver.
    /// </summary>
    public static void FixedUpdate(Scene scene, float deltaTime)
    {
        foreach ((Entity _, ScriptComponent scriptComp) in scene.ViewActive<ScriptComponent>())
        {
            foreach (EntityBehaviour script in scriptComp.Scripts.ToList())
            {
                // ActiveLastFrame ensures OnEnable has already run: on a re-enable frame FixedUpdate (which
                // runs before the Scripts system) waits until the next frame rather than preceding OnEnable.
                if (script.Faulted || !script.ActiveLastFrame) continue;

                try
                {
                    script.OnFixedUpdate(deltaTime);
                }
                catch (Exception ex)
                {
                    Quarantine(script, "fixed_update", ex);
                }
            }
        }
    }

    /// <summary>
    /// Invokes <see cref="EntityBehaviour.OnValidate"/> on a single script, guarded so a throwing handler is
    /// quarantined rather than crashing the editor. Called by the inspector when a serialized field changes.
    /// </summary>
    internal static void InvokeValidate(EntityBehaviour script)
    {
        if (script.Faulted) return;

        try
        {
            script.OnValidate();
        }
        catch (Exception ex)
        {
            Quarantine(script, "validate", ex);
        }
    }

    public static void ImGuiRender(Scene scene)
    {
        foreach (Entity entity in scene.View<ScriptComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var scriptComp = entity.GetComponent<ScriptComponent>();
            if (!scriptComp.Enabled) continue;

            foreach (EntityBehaviour script in scriptComp.Scripts.ToList())
            {
                if (script.Faulted || !script.Started) continue;

                try
                {
                    script.OnImGuiRender();
                }
                catch (Exception ex)
                {
                    Quarantine(script, "imgui_render", ex);
                }
            }
        }
    }

    public static void DestroyAll(Scene scene)
    {
        foreach (Entity entity in scene.View<ScriptComponent>())
        {
            foreach (EntityBehaviour script in entity.GetComponent<ScriptComponent>().Scripts.ToList())
            {
                if (!script.Started)
                {
                    continue;
                }

                // Teardown is still guarded so a throwing hook cannot abort the shutdown or the cleanup of
                // the remaining scripts. A still-enabled script gets its OnDisable first, mirroring OnEnable.
                if (script.ActiveLastFrame)
                {
                    script.ActiveLastFrame = false;
                    try
                    {
                        script.OnDisable();
                    }
                    catch (Exception ex)
                    {
                        Log.CoreError(
                            "Script '{0}' threw from OnDisable; ignoring. {1}",
                            script.GetType().Name,
                            ex);
                    }
                }

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
    /// Invokes <paramref name="action"/> for each started, non-faulted script on an active entity whose
    /// script component is enabled, applying the same guard/quarantine as the per-frame update. Used to
    /// deliver out-of-band callbacks (such as physics collision events) without duplicating the gating.
    /// </summary>
    internal static void ForEachLiveScript(Entity entity, string phase, Action<EntityBehaviour> action)
    {
        if (!entity.IsValid || !entity.IsActiveInHierarchy()) return;
        if (!entity.TryGetComponent(out ScriptComponent? scriptComp) || !scriptComp.Enabled) return;

        foreach (EntityBehaviour script in scriptComp.Scripts.ToList())
        {
            if (script.Faulted || !script.Started) continue;

            try
            {
                action(script);
            }
            catch (Exception ex)
            {
                Quarantine(script, phase, ex);
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
