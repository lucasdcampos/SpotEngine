using System.Numerics;
using System.Text.Json.Nodes;
using Spot.Physics;
using Spot.Scenes;
using Xunit;

namespace Spot.Engine.Tests;

public class ComponentSerializationTests
{
    [Fact]
    public void RoundTrips_ScalarsEnumsAndVectors()
    {
        var camera = new CameraComponent
        {
            Primary = true,
            ProjectionType = SceneCameraProjection.Perspective,
            FieldOfView = 60f,
            ZoomLevel = 3.5f,
            NearClip = 0.2f,
            FarClip = 500f,
            BackgroundColor = new Vector4(0.1f, 0.2f, 0.3f, 1f),
        };

        JsonObject json = ComponentSerialization.Serialize(camera);
        var loaded = (CameraComponent)ComponentSerialization.Deserialize(typeof(CameraComponent), json);

        Assert.True(loaded.Primary);
        Assert.Equal(SceneCameraProjection.Perspective, loaded.ProjectionType);
        Assert.Equal(60f, loaded.FieldOfView);
        Assert.Equal(3.5f, loaded.ZoomLevel);
        Assert.Equal(0.2f, loaded.NearClip);
        Assert.Equal(500f, loaded.FarClip);
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 1f), loaded.BackgroundColor);
    }

    [Fact]
    public void SerializesEnumsAsIntAndVectorsAsArrays()
    {
        var light = new LightComponent { Type = LightType.Point, Color = new Vector3(1, 0, 0.5f), Range = 12f };

        JsonObject json = ComponentSerialization.Serialize(light);

        Assert.Equal((int)LightType.Point, json["Type"]!.GetValue<int>());
        JsonArray color = json["Color"]!.AsArray();
        Assert.Equal(3, color.Count);
        Assert.Equal(1f, color[0]!.GetValue<float>());
        Assert.Equal(0.5f, color[2]!.GetValue<float>());
    }

    [Fact]
    public void PreservesAssetPathProperty()
    {
        // primitive:/editor: pseudo-paths pass through MakeRelative unchanged, so this needs no asset root.
        var mesh = new MeshComponent { ModelPath = "primitive:Cube", Color = Vector4.One };

        JsonObject json = ComponentSerialization.Serialize(mesh);
        Assert.Equal("primitive:Cube", json["ModelPath"]!.GetValue<string>());

        var loaded = (MeshComponent)ComponentSerialization.Deserialize(typeof(MeshComponent), json);
        Assert.Equal("primitive:Cube", loaded.ModelPath);
    }

    [Fact]
    public void PersistsSerializeHiddenSubmeshIndex()
    {
        // SubmeshIndex is [HideInInspector] but [SerializeHidden] — authored in code (by ModelInstantiator for
        // a multi-part model) and must survive save/load, or a skinned part loses which submesh it draws.
        var mesh = new MeshComponent { ModelPath = "primitive:Cube", SubmeshIndex = 2 };

        JsonObject json = ComponentSerialization.Serialize(mesh);
        Assert.Equal(2, json["SubmeshIndex"]!.GetValue<int>());

        var loaded = (MeshComponent)ComponentSerialization.Deserialize(typeof(MeshComponent), json);
        Assert.Equal(2, loaded.SubmeshIndex);
    }

    [Fact]
    public void SkipsInspectorHiddenRuntimeState()
    {
        var cc = new CharacterController3DComponent { WalkSpeed = 5f };
        cc.Yaw = 42f;          // [HideInInspector] runtime state — must not be persisted.
        cc.IsGrounded = true;

        JsonObject json = ComponentSerialization.Serialize(cc);

        Assert.True(json.ContainsKey("WalkSpeed"));
        Assert.False(json.ContainsKey("Yaw"));
        Assert.False(json.ContainsKey("IsGrounded"));

        var loaded = (CharacterController3DComponent)ComponentSerialization.Deserialize(typeof(CharacterController3DComponent), json);
        Assert.Equal(5f, loaded.WalkSpeed);
        Assert.Equal(0f, loaded.Yaw);
    }

    [Fact]
    public void Registry_ResolvesKeysIncludingLegacyAlias()
    {
        Assert.True(ComponentSerialization.TryResolveKey("Sprite", out var sprite));
        Assert.Equal(typeof(Sprite2DComponent), sprite);

        Assert.True(ComponentSerialization.TryResolveKey("MeshRenderer", out var mesh));
        Assert.Equal(typeof(MeshComponent), mesh);

        // Legacy directional-light key maps onto the unified LightComponent.
        Assert.True(ComponentSerialization.TryResolveKey("DirectionalLight", out var legacy));
        Assert.Equal(typeof(LightComponent), legacy);
    }
}
