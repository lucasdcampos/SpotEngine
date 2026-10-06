using Spot.Engine;

namespace Spot.Engine.Scenes;

/// <summary>
/// Runs the lifecycle hooks of user components (see <see cref="Component.IsUserComponent"/>) for the active
/// scene: starts newly attached ones (<see cref="Component.OnStart"/>), fires the enable/disable edges, and
/// drives <see cref="Component.OnUpdate"/>, <see cref="Component.OnLateUpdate"/> and
/// <see cref="Component.OnFixedUpdate"/>. Each hook runs inside its own guard: a component that throws is
/// quarantined (logged once and skipped from then on) so it neither crashes the engine nor stops the others.
/// </summary>
internal static class ComponentSystem
{
    /// <summary>
    /// The per-frame pass: starts, enables/disables and updates every user component (advancing its coroutines
    /// and invokes), then runs <see cref="Component.OnLateUpdate"/> once every component has updated.
    /// </summary>
    public static void Update(Scene scene, float deltaTime)
    {
        UpdatePass(scene, deltaTime);
        LateUpdate(scene, deltaTime);
    }

    // Iterates a snapshot so a component that spawns or destroys entities cannot invalidate the loop, and
    // observes disabled components too so they fire OnDisable on the frame they go inactive.
    private static void UpdatePass(Scene scene, float deltaTime)
    {
        foreach (Component component in scene.UserComponents)
        {
            if (component.Faulted || component.Detached)
            {
                continue;
            }

            try
            {
                if (IsActive(component))
                {
                    if (!component.Started)
                    {
                        component.OnStart();
                        component.Started = true;
                    }

                    // Enable edge: fires on first activation (after OnStart) and every re-enable after.
                    if (!component.ActiveLastFrame)
                    {
                        component.OnEnable();
                        component.ActiveLastFrame = true;
                    }

                    component.OnUpdate(deltaTime);

                    // Advance coroutines/invokes after the component's own update, sharing its guard so a
                    // scheduling fault is quarantined the same way. Unscaled delta drives realtime waits.
                    component.TickScheduling(deltaTime, Time.UnscaledDeltaTime);
                }
                else if (component.ActiveLastFrame)
                {
                    // Disable edge: the entity or the component was turned off this frame.
                    component.ActiveLastFrame = false;
                    component.OnDisable();
                }
            }
            catch (Exception ex)
            {
                Quarantine(component, "update", ex);
            }
        }
    }

    /// <summary>
    /// Runs <see cref="Component.OnLateUpdate"/> after every component's <see cref="Component.OnUpdate"/> this
    /// frame, so it observes their changes (a follow camera sees its target's moved position).
    /// </summary>
    private static void LateUpdate(Scene scene, float deltaTime) =>
        ForEachLive(scene, "late_update", component => component.OnLateUpdate(deltaTime));

    /// <summary>
    /// Runs <see cref="Component.OnFixedUpdate"/> for every started, active component. Runs just before the
    /// physics step so components can apply forces or move bodies in step with the solver. On a re-enable frame
    /// it waits for the next frame rather than preceding <see cref="Component.OnEnable"/>.
    /// </summary>
    public static void FixedUpdate(Scene scene, float deltaTime) =>
        ForEachLive(scene, "fixed_update", component => component.OnFixedUpdate(deltaTime));

    /// <summary>Runs <see cref="Component.OnImGuiRender"/> for every started, active component.</summary>
    public static void ImGuiRender(Scene scene) =>
        ForEachLive(scene, "imgui_render", component => component.OnImGuiRender());

    /// <summary>
    /// Invokes <see cref="Component.OnValidate"/> on a single component, guarded so a throwing handler is
    /// quarantined rather than crashing the editor. Called by the inspector when a serialized field changes.
    /// </summary>
    public static void InvokeValidate(Component component)
    {
        if (component.Faulted) return;

        try
        {
            component.OnValidate();
        }
        catch (Exception ex)
        {
            Quarantine(component, "validate", ex);
        }
    }

    /// <summary>
    /// Invokes <paramref name="action"/> for each started, non-faulted user component on
    /// <paramref name="entity"/> (when the entity is active), applying the same guard and quarantine as the
    /// per-frame update. Used to deliver out-of-band callbacks such as physics collision events.
    /// </summary>
    public static void ForEachLive(Entity entity, string phase, Action<Component> action)
    {
        if (!entity.IsValid || !entity.IsActiveInHierarchy()) return;

        // Copy: a handler may add or remove components on its own entity.
        Component[] components = entity.Components.ToArray();
        foreach (Component component in components)
        {
            if (!component.IsUserComponent || !IsLive(component)) continue;

            try
            {
                action(component);
            }
            catch (Exception ex)
            {
                Quarantine(component, phase, ex);
            }
        }
    }

    /// <summary>
    /// Tears down every started user component in the scene (<see cref="Component.OnDisable"/> if it was
    /// enabled, then <see cref="Component.OnDestroy"/>). Called when the scene is left or play mode stops.
    /// </summary>
    public static void DestroyAll(Scene scene)
    {
        foreach (Component component in scene.UserComponents)
        {
            Teardown(component);
        }
    }

    /// <summary>
    /// Ends a user component's life: a still-enabled one gets <see cref="Component.OnDisable"/>, a started one
    /// <see cref="Component.OnDestroy"/>. Each hook is guarded so a throwing teardown cannot abort the removal or
    /// the destruction of the rest of the tree. The component is marked detached so a frame snapshot that still
    /// holds it skips it; re-attaching the same instance starts it afresh.
    /// </summary>
    public static void Teardown(Component component)
    {
        if (!component.IsUserComponent)
        {
            return;
        }

        component.Detached = true;
        if (!component.Started)
        {
            return;
        }

        if (component.ActiveLastFrame)
        {
            component.ActiveLastFrame = false;
            try
            {
                component.OnDisable();
            }
            catch (Exception ex)
            {
                Log.CoreError("Component '{0}' threw from OnDisable; ignoring. {1}", component.GetType().Name, ex);
            }
        }

        component.Started = false;
        try
        {
            component.OnDestroy();
        }
        catch (Exception ex)
        {
            Log.CoreError("Component '{0}' threw from OnDestroy; ignoring. {1}", component.GetType().Name, ex);
        }
    }

    private static void ForEachLive(Scene scene, string phase, Action<Component> action)
    {
        foreach (Component component in scene.UserComponents)
        {
            if (!IsLive(component) || !IsActive(component)) continue;

            try
            {
                action(component);
            }
            catch (Exception ex)
            {
                Quarantine(component, phase, ex);
            }
        }
    }

    // A component the update pass has started and enabled, and that has not faulted or been removed since.
    private static bool IsLive(Component component) =>
        !component.Faulted && !component.Detached && component.Started && component.ActiveLastFrame;

    private static bool IsActive(Component component) =>
        component.Enabled && component.Entity.IsActiveInHierarchy();

    /// <summary>
    /// Disables a component that threw from a lifecycle hook and logs the fault once. The component is skipped
    /// on later frames so the failure is not repeated every frame.
    /// </summary>
    private static void Quarantine(Component component, string phase, Exception ex)
    {
        component.Faulted = true;
        Log.CoreError(
            "Component '{0}' threw during {1} and was disabled. {2}",
            component.GetType().Name,
            phase,
            ex);
    }
}
