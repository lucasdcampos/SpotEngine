namespace Spot.Scenes;

/// <summary>
/// Holds the active <see cref="Scene"/> and switches between scenes. The application drives the
/// active scene each frame; call <see cref="Load"/> from anywhere (for example a menu button) to
/// request a switch, which is applied at the next frame boundary.
/// </summary>
public static class SceneManager
{
    private static Scene? s_current;
    private static Scene? s_pending;

    /// <summary>
    /// Gets the currently active scene, if any.
    /// </summary>
    public static Scene? Current => s_current;

    /// <summary>
    /// Requests a switch to the given scene. The switch takes effect at the start of the next frame.
    /// </summary>
    /// <param name="scene">The scene to switch to.</param>
    public static void Load(Scene scene) => s_pending = scene;

    /// <summary>
    /// Applies a pending scene switch, exiting the old scene and entering the new one.
    /// </summary>
    internal static void ApplyPendingSwitch()
    {
        if (s_pending is null)
        {
            return;
        }

        if (s_current is not null)
        {
            ScriptSystem.DestroyAll(s_current);
            s_current.OnExit();
        }

        s_current = s_pending;
        s_pending = null;
        s_current.OnEnter();
    }

    internal static void DispatchEvent(Spot.Events.Event e) => s_current?.OnEvent(e);

    internal static void Update(float deltaTime)
    {
        if (s_current is null)
        {
            return;
        }

        s_current.OnUpdate(deltaTime);
        ScriptSystem.Update(s_current, deltaTime);
        s_current.FlushDestroyed();
    }

    internal static void Render() => s_current?.OnRender();

    internal static void ImGuiRender() => s_current?.OnImGuiRender();

    internal static void Shutdown()
    {
        if (s_current is not null)
        {
            ScriptSystem.DestroyAll(s_current);
            s_current.OnExit();
        }

        s_current = null;
        s_pending = null;
    }
}
