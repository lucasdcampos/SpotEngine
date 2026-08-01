using System;

namespace Spot.Scenes;

public class PostProcessingComponent : Component
{
    public float Exposure { get; set; } = 1.0f;
    public float Gamma { get; set; } = 2.2f;
    
    public bool EnableVignette { get; set; } = true;
    public float VignetteIntensity { get; set; } = 0.25f;
    
    public bool EnableBloom { get; set; } = true;
    public float BloomThreshold { get; set; } = 1.0f;
    public float BloomIntensity { get; set; } = 1.0f;
}
