using Spot.Engine.Scenes;
using System.Numerics;

namespace Spot.Engine;

[ComponentMenu("Skybox", Order = 95, Category = "Environment")]
[SceneComponent("Skybox")]
public sealed class Skybox : Component
{
    [InspectorColor] public Vector3 SkyColor { get; set; } = new Vector3(0.6f, 0.8f, 1.0f);
    [InspectorColor] public Vector3 GroundColor { get; set; } = new Vector3(0.15f, 0.15f, 0.15f);
}
