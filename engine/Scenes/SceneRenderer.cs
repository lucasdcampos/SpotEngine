using System.Numerics;

namespace Spot.Scenes;

/// <summary>
/// The callback that draws a scene's entities through a camera — the seam between the neutral scene layer
/// and a platform's render pipeline.
/// </summary>
/// <param name="scene">The scene whose entities are drawn.</param>
/// <param name="viewProjection">The camera's view-projection matrix.</param>
/// <param name="cameraPosition">The camera's world position.</param>
public delegate void SceneRenderCallback(Scene scene, Matrix4x4 viewProjection, Vector3 cameraPosition);

/// <summary>
/// The installed scene-render pipeline. <see cref="Scene.OnRender"/> issues its draw through here instead of
/// binding to a concrete renderer, so the host chooses the pipeline: the desktop installs the full 3D-first
/// <see cref="RenderSystem"/> (meshes, lighting, shadows, HDR/post); the browser MVP installs a slim 2D path.
/// When no pipeline is installed, rendering is a no-op (the scene still simulates).
/// </summary>
public static class SceneRenderer
{
    /// <summary>Gets or sets the callback that draws a scene. Installed once by the host at startup.</summary>
    public static SceneRenderCallback? Callback { get; set; }

    /// <summary>Draws the scene through the installed pipeline, or does nothing when none is installed.</summary>
    public static void Render(Scene scene, Matrix4x4 viewProjection, Vector3 cameraPosition) =>
        Callback?.Invoke(scene, viewProjection, cameraPosition);
}
