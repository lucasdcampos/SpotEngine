using System.Numerics;

namespace Spot.Scenes;

[ComponentMenu("Dynamic Clouds", Order = 90)]
public sealed class DynamicCloudsComponent : Component
{
    [InspectorColor]
    public Vector3 ColorTop { get; set; } = new Vector3(1.0f, 1.0f, 1.0f);

    [InspectorColor]
    public Vector3 ColorBottom { get; set; } = new Vector3(0.8f, 0.85f, 0.9f);

    [InspectorRange(0.0f, 10.0f, 0.01f)]
    public float Speed { get; set; } = 1.0f;

    [InspectorRange(0.0f, 1.0f, 0.01f)]
    public float Density { get; set; } = 0.57f;

    [InspectorRange(0.0f, 10.0f, 0.01f)]
    public float Height { get; set; } = 0.3f;

    [InspectorRange(0.0f, 1.0f, 0.01f)]
    public float Opacity { get; set; } = 0.6f;

    [InspectorRange(0.0f, 5.0f, 0.01f)]
    public float Volume { get; set; } = 1.35f;
}
