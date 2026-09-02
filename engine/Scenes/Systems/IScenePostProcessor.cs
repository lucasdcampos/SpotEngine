namespace Spot.Scenes;

/// <summary>
/// The seam through which <see cref="RenderSystem"/> applies full-screen post-processing (HDR capture, bloom,
/// tone mapping, FXAA). The desktop host installs an implementation; a host that leaves
/// <see cref="RenderSystem.PostProcessor"/> null renders the scene straight to the screen with no post — the
/// browser's current behavior. This keeps the shared <see cref="RenderSystem"/> backend-neutral: the parts of
/// the pipeline that still depend on offscreen render targets and bloom/tone-mapping shaders live behind this
/// seam instead of being compiled into every target.
/// </summary>
public interface IScenePostProcessor
{
    /// <summary>
    /// Begins capturing the scene into an offscreen HDR target before it is drawn. Returns <see langword="true"/>
    /// when capture started (the caller must then draw the scene and call <see cref="Resolve"/>); returns
    /// <see langword="false"/> to skip post-processing this frame (for example a zero-sized viewport), in which
    /// case the scene renders directly to the screen.
    /// </summary>
    /// <param name="settings">The active post-processing settings.</param>
    /// <returns>Whether the scene is being captured for a later <see cref="Resolve"/>.</returns>
    bool Begin(PostProcessingComponent settings);

    /// <summary>
    /// Composites the captured scene back to the screen, applying bloom, tone mapping and FXAA per the
    /// settings. Called after the scene has been drawn, only when <see cref="Begin"/> returned true.
    /// </summary>
    /// <param name="settings">The active post-processing settings.</param>
    void Resolve(PostProcessingComponent settings);
}
