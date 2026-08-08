using System.IO;
using Spot.Assets;
using Spot.Core;

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
    /// Loads a scene by reference and requests a switch to it, the runtime path for menu → level →
    /// game-over flow. The reference is a <c>guid:</c> asset reference, a path relative to the asset
    /// root (for example <c>Scenes/Level1.sptscene</c>), or an absolute path. Returns
    /// <see langword="false"/> — logging, never throwing — when the scene cannot be found or read, in
    /// which case the current scene keeps running.
    /// </summary>
    /// <param name="sceneReference">The scene's guid reference or path.</param>
    /// <returns><see langword="true"/> if a switch was queued; otherwise <see langword="false"/>.</returns>
    public static bool Load(string sceneReference)
    {
        Scene? scene = LoadSceneFromReference(sceneReference);
        if (scene is null)
        {
            return false;
        }

        Load(scene);
        return true;
    }

    // Resolves a scene reference to an absolute file and deserializes it into a fresh scene. Returns null
    // (after logging) when the reference cannot be resolved, the file is missing, or parsing fails.
    private static Scene? LoadSceneFromReference(string sceneReference)
    {
        if (string.IsNullOrWhiteSpace(sceneReference))
        {
            Log.CoreError("Cannot load a scene from an empty reference.");
            return null;
        }

        string path = AssetPath.ResolveContent(sceneReference) ?? AssetPath.Resolve(sceneReference);
        if (!File.Exists(path))
        {
            Log.CoreError("Scene not found: {0}", sceneReference);
            return null;
        }

        var scene = new Scene();
        var serializer = new SceneSerializer(scene);
        return serializer.Deserialize(path) ? scene : null;
    }

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
            // Carry persistent entities (DontDestroyOnLoad) into the incoming scene before the old one
            // is torn down, so their live state survives and their scripts are not destroyed.
            s_current.MigratePersistentEntitiesTo(s_pending);
            ScriptSystem.DestroyAll(s_current);
            s_current.OnExit();
            s_current.TeardownPhysics();
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

        s_current.UpdateRuntime(deltaTime);
    }

    internal static void Render() => s_current?.OnRender();

    internal static void ImGuiRender() => s_current?.OnImGuiRender();

    internal static void Shutdown()
    {
        if (s_current is not null)
        {
            ScriptSystem.DestroyAll(s_current);
            s_current.OnExit();
            s_current.TeardownPhysics();
        }

        s_current = null;
        s_pending = null;
    }
}
