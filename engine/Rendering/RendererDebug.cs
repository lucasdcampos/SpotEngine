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
}
