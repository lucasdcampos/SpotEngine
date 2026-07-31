using System.Numerics;

namespace Spot.Scenes;

public sealed class DynamicCloudsComponent : Component
{
    public Vector3 ColorTop { get; set; } = new Vector3(1.0f, 1.0f, 1.0f);
    public Vector3 ColorBottom { get; set; } = new Vector3(0.8f, 0.85f, 0.9f);
    public float Speed { get; set; } = 1.0f;
    public float Density { get; set; } = 0.57f;
    public float Height { get; set; } = 0.3f;
    public float Opacity { get; set; } = 0.6f;
    public float Volume { get; set; } = 1.35f;
}
