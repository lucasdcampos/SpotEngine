namespace Spot.Rendering;

/// <summary>
/// Global, engine-wide rendering pipeline settings. These are quality/pipeline knobs that apply to
/// every scene, as opposed to the per-scene artistic controls on a <c>PostProcessingComponent</c>.
/// </summary>
public static class RenderSettings
{
    /// <summary>
    /// Whether the scene is rendered into a high-dynamic-range buffer and tone-mapped, even when the
    /// scene has no <c>PostProcessingComponent</c>. When on, a scene without one is composited with
    /// sensible defaults (ACES tone mapping and FXAA, no bloom or vignette) so highlights roll off and
    /// edges are anti-aliased everywhere. When off, a scene with no post-processing renders directly to
    /// the target in 8-bit, as it did before HDR existed.
    /// </summary>
    public static bool Hdr { get; set; } = true;
}
