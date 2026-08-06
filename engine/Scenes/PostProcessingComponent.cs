using System;

namespace Spot.Scenes;

/// <summary>
/// The tone-mapping operator that compresses the scene's high-dynamic-range colors into the
/// displayable [0,1] range during post-processing.
/// </summary>
public enum TonemapMode
{
    /// <summary>No curve; colors are simply clamped. Bright regions harshly clip to white.</summary>
    None = 0,

    /// <summary>Reinhard (<c>c / (1 + c)</c>): gentle, desaturates highlights, never fully reaches white.</summary>
    Reinhard = 1,

    /// <summary>ACES filmic approximation: film-like contrast and highlight roll-off. The default.</summary>
    Aces = 2,
}

[ComponentMenu("Post Processing", Order = 110)]
[SceneComponent("PostProcessing")]
public class PostProcessingComponent : Component
{
    /// <summary>The tone-mapping curve applied to the HDR scene before gamma correction.</summary>
    public TonemapMode Tonemap { get; set; } = TonemapMode.Aces;

    [InspectorRange(0.0f, 10.0f, 0.05f)]
    public float Exposure { get; set; } = 1.0f;

    [InspectorRange(0.0f, 10.0f, 0.05f)]
    public float Gamma { get; set; } = 2.2f;

    public bool EnableVignette { get; set; } = true;

    [InspectorRange(0.0f, 5.0f, 0.01f)]
    [ShowIf(nameof(EnableVignette), true)]
    public float VignetteIntensity { get; set; } = 0.25f;

    /// <summary>Applies FXAA (fast approximate anti-aliasing) to the final tone-mapped image.</summary>
    public bool EnableFXAA { get; set; } = true;

    public bool EnableBloom { get; set; } = true;

    [InspectorRange(0.0f, 10.0f, 0.05f)]
    [ShowIf(nameof(EnableBloom), true)]
    public float BloomThreshold { get; set; } = 1.0f;

    [InspectorRange(0.0f, 5.0f, 0.05f)]
    [ShowIf(nameof(EnableBloom), true)]
    public float BloomIntensity { get; set; } = 1.0f;
}
