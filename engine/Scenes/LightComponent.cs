using System.Numerics;

namespace Spot.Scenes;

public enum LightType
{
    Directional,
    Point
}

[ComponentMenu("Light", Order = 80)]
[SceneComponent("Light")]
public sealed class LightComponent : Component
{
    public LightType Type { get; set; } = LightType.Directional;

    [InspectorColor]
    public Vector3 Color { get; set; } = Vector3.One;

    [InspectorRange(0.0f, 10.0f, 0.05f)]
    public float Intensity { get; set; } = 1.0f;

    // Directional light specific
    [InspectorRange(0.0f, 1.0f, 0.01f)]
    [ShowIf(nameof(Type), LightType.Directional)]
    public float AmbientIntensity { get; set; } = 0.3f;

    [ShowIf(nameof(Type), LightType.Directional)]
    public bool CastShadows { get; set; } = true;

    // Point light specific
    [InspectorRange(0.0f, 100.0f, 0.1f)]
    [ShowIf(nameof(Type), LightType.Point)]
    public float Range { get; set; } = 10.0f;
}
