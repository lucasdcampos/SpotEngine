using System.Numerics;

namespace Spot.Scenes;

[ComponentMenu("Skybox", Order = 95)]
public sealed class SkyboxComponent : Component
{
    [InspectorColor] public Vector3 SkyColor { get; set; } = new Vector3(0.6f, 0.8f, 1.0f);
    [InspectorColor] public Vector3 GroundColor { get; set; } = new Vector3(0.15f, 0.15f, 0.15f);
}
