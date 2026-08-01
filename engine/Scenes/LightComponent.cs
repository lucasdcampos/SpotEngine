using System.Numerics;

namespace Spot.Scenes;

public enum LightType
{
    Directional,
    Point
}

public sealed class LightComponent : Component
{
    public LightType Type { get; set; } = LightType.Directional;
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;
    
    // Directional light specific
    public float AmbientIntensity { get; set; } = 0.3f;
    public bool CastShadows { get; set; } = true;
    
    // Point light specific
    public float Range { get; set; } = 10.0f;
}
