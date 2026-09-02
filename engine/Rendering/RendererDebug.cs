namespace Spot.Rendering;

/// <summary>
/// Contains global graphics debugging settings.
/// </summary>
public static class RendererDebug
{
    /// <summary>
    /// Gets or sets whether everything should be rendered fullbright, ignoring lighting.
    /// </summary>
    public static bool Fullbright { get; set; } = false;

    /// <summary>
    /// Gets or sets whether 3D meshes should be rendered as wireframes.
    /// </summary>
    public static bool Wireframe { get; set; } = false;

    /// <summary>
    /// Gets or sets whether frustum culling is disabled, forcing every mesh to be drawn regardless of
    /// visibility. Off by default (culling on); flip it on to A/B the effect or rule culling out when
    /// diagnosing missing geometry.
    /// </summary>
    public static bool DisableFrustumCulling { get; set; } = false;

    /// <summary>
    /// Gets the number of mesh entities drawn in the last main 3D pass (survived frustum culling).
    /// Reset at the start of each <see cref="Spot.Scenes.RenderSystem"/> render.
    /// </summary>
    public static int VisibleMeshCount { get; internal set; }

    /// <summary>
    /// Gets the number of mesh entities skipped by frustum culling in the last main 3D pass.
    /// Reset at the start of each <see cref="Spot.Scenes.RenderSystem"/> render.
    /// </summary>
    public static int CulledMeshCount { get; internal set; }
}
