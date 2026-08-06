namespace Spot.Rendering;

/// <summary>
/// Global, engine-wide rendering pipeline settings. These are quality/pipeline knobs that apply to
/// every scene, as opposed to the per-scene artistic controls on a <c>PostProcessingComponent</c>.
/// </summary>
public static class RenderSettings
{
    /// <summary>
    /// Whether the scene is rendered into a high-dynamic-range buffer and tone-mapped, even when the
    /// scene has no <c>PostProcessingComponent</c>. When on, a scene without one is composited with the
    /// full default look — the same defaults a freshly added component carries: ACES tone mapping,
    /// FXAA, threshold-gated bloom, and only a very faint vignette. The baseline exalts the engine's
    /// lighting rather than dressing it up with heavy stylistic filters; adding the component is for
    /// customizing that look, not switching quality on. When off, a scene with no post-processing
    /// renders directly to the target in 8-bit, as it did before HDR existed.
    /// </summary>
    public static bool Hdr { get; set; } = true;
}
