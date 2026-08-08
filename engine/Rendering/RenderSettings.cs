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

    /// <summary>
    /// Whether directional lights cast real-time shadows at all. A global kill switch on top of each
    /// light's own <c>CastShadows</c>; turning it off skips the shadow pass entirely.
    /// </summary>
    public static bool Shadows { get; set; } = true;

    /// <summary>
    /// The side length, in world units, of the square region around the camera that receives directional
    /// shadows. The shadow frustum follows the camera, so shadows work everywhere in the world (unlike the
    /// old origin-anchored box) — this only bounds how far from the viewer they reach. Larger covers more
    /// ground but spreads the shadow map thinner, so shadows soften/blockier; smaller keeps them crisp.
    /// </summary>
    public static float ShadowDistance { get; set; } = 100.0f;

    /// <summary>
    /// The resolution (per side) of the directional shadow map. Higher is sharper at a memory/fill cost.
    /// Changing it rebuilds the shadow map on the next frame. Clamped to a sane power-of-two-ish range.
    /// </summary>
    public static int ShadowMapResolution { get; set; } = 2048;
}
