using System;
using System.Numerics;
using Spot.Rendering;
using Spot.Scenes;
using Xunit;

namespace Spot.Engine.Tests;

// Guards the reflection-based serializer against regressions in the on-disk format: it must still read
// scenes written by the old hand-written serializer (null component slots, the legacy "DirectionalLight"
// key) and round-trip the full range of component data.
public class SceneSerializerBackCompatTests
{
    [Fact]
    public void Reads_LegacyFormatWithNullSlotsAndDirectionalLightAlias()
    {
        // Shaped like a scene the previous serializer produced: every component slot present, most null,
        // and a directional light stored under the old "DirectionalLight" key rather than "Light".
        const string json = """
        {
          "Entities": [
            {
              "Tag": { "Name": "Sun", "Enabled": true },
              "Transform": { "Position": [0, 10, 0], "Rotation": [-16.9, 45, 0], "Scale": [1, 1, 1], "Enabled": true },
              "Sprite": null,
              "MeshRenderer": null,
              "Scripts": null,
              "Camera": null,
              "PhysicsBody2D": null,
              "DirectionalLight": { "Color": [0.94, 0.95, 0.87], "Intensity": 2, "AmbientIntensity": 0.3, "Enabled": true },
              "Light": null,
              "Skybox": { "SkyColor": [0.2, 0.4, 0.6], "GroundColor": [0.25, 0.39, 0.46], "Enabled": true },
              "Children": null
            }
          ]
        }
        """;

        var scene = new Scene();
        Assert.True(new SceneSerializer(scene).DeserializeFromString(json));

        Entity sun = FindByName(scene, "Sun");
        Assert.Equal(new Vector3(0, 10, 0), sun.GetComponent<TransformComponent>().Position);

        // The legacy "DirectionalLight" slot maps onto LightComponent with Type defaulting to Directional.
        var light = sun.GetComponent<LightComponent>();
        Assert.Equal(LightType.Directional, light.Type);
        Assert.Equal(2f, light.Intensity);

        Assert.True(sun.HasComponent<SkyboxComponent>());
        // A null slot must not create the component.
        Assert.False(sun.HasComponent<CameraComponent>());
        Assert.False(sun.HasComponent<Spot.Physics.PhysicsBody2DComponent>());
    }

    [Fact]
    public void RoundTrips_MeshSkyboxPostProcessingAndNestedChild()
    {
        var scene = new Scene();
        var ground = scene.Instantiate("Ground");
        ground.AddComponent(new MeshComponent { ModelPath = "primitive:Plane", Color = new Vector4(0.5f, 0.6f, 0.7f, 1f) });
        ground.AddComponent(new SkyboxComponent { SkyColor = new Vector3(0.1f, 0.2f, 0.3f) });
        ground.AddComponent(new PostProcessingComponent { Exposure = 1.5f, EnableBloom = false });

        var detail = scene.Instantiate("Detail");
        detail.SetParent(ground);
        detail.AddComponent(new DynamicCloudsComponent { Density = 0.42f });

        string json = new SceneSerializer(scene).SerializeToString();

        var loaded = new Scene();
        Assert.True(new SceneSerializer(loaded).DeserializeFromString(json));

        Entity g = FindByName(loaded, "Ground");
        var mesh = g.GetComponent<MeshComponent>();
        Assert.Equal("primitive:Plane", mesh.ModelPath);
        Assert.Equal(new Vector4(0.5f, 0.6f, 0.7f, 1f), mesh.Color);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), g.GetComponent<SkyboxComponent>().SkyColor);

        var pp = g.GetComponent<PostProcessingComponent>();
        Assert.Equal(1.5f, pp.Exposure);
        Assert.False(pp.EnableBloom);

        Entity d = FindByName(loaded, "Detail");
        Assert.Equal("Ground", d.Parent!.Value.Name);
        Assert.Equal(0.42f, d.GetComponent<DynamicCloudsComponent>().Density);
    }

    private static Entity FindByName(Scene scene, string name)
    {
        foreach (var e in scene.View<LabelComponent>())
        {
            if (e.Name == name)
            {
                return e;
            }
        }

        throw new InvalidOperationException($"Entity '{name}' was not found.");
    }
}
