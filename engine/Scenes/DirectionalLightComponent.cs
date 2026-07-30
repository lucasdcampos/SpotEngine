using System.Numerics;

namespace Spot.Scenes;

public sealed class DirectionalLightComponent
{
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;
    public float AmbientIntensity { get; set; } = 0.3f;
}
