using System;

namespace Spot.Scenes;

[ComponentMenu("Post Processing", Order = 110)]
public class PostProcessingComponent : Component
{
    [InspectorRange(0.0f, 10.0f, 0.05f)]
    public float Exposure { get; set; } = 1.0f;

    [InspectorRange(0.0f, 10.0f, 0.05f)]
    public float Gamma { get; set; } = 2.2f;

    public bool EnableVignette { get; set; } = true;

    [InspectorRange(0.0f, 5.0f, 0.01f)]
    [ShowIf(nameof(EnableVignette), true)]
    public float VignetteIntensity { get; set; } = 0.25f;

    public bool EnableBloom { get; set; } = true;

    [InspectorRange(0.0f, 10.0f, 0.05f)]
    [ShowIf(nameof(EnableBloom), true)]
    public float BloomThreshold { get; set; } = 1.0f;

    [InspectorRange(0.0f, 5.0f, 0.05f)]
    [ShowIf(nameof(EnableBloom), true)]
    public float BloomIntensity { get; set; } = 1.0f;
}
