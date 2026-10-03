using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Spot.Engine.Scenes;

namespace Spot.Engine.Tests;

// A user component with tunables of every common kind, including a reference to another entity.
public sealed class SerializedMover : Component
{
    public float Speed = 1.0f;
    public int Lives;
    public Vector3 Direction;
    public Entity Target;
    public float Multiplier { get; set; } = 1.0f;
}

public sealed class SerializedTagAlong : Component
{
}

// Resolved only through the registry by its guid, to verify rename-safe references.
public sealed class GuidOnlyComponent : Component
{
    public int Value;
}

public class UserComponentSerializationTests
{
    private const string LateGuid = "late-component-guid";

    private sealed class GuidProvider : IScriptProvider
    {
        public IEnumerable<ScriptDescriptor> GetScripts() =>
            new[] { new ScriptDescriptor(LateGuid, nameof(GuidOnlyComponent), typeof(GuidOnlyComponent), () => new GuidOnlyComponent()) };
    }

    private static Scene RoundTrip(Scene scene)
    {
        string json = new SceneSerializer(scene).SerializeToString();
        var loaded = new Scene();
        Assert.True(new SceneSerializer(loaded).DeserializeFromString(json));
        return loaded;
    }

    private static Entity FindByName(Scene scene, string name) =>
        scene.View<LabelComponent>().Single(e => e.Name == name);

    [Fact]
    public void UserComponent_RoundTripsFieldsEnabledStateAndEntityReferences()
    {
        var scene = new Scene();
        Entity target = scene.Instantiate("Target");
        Entity mover = scene.Instantiate("Mover");
        mover.AddComponent(new SerializedMover
        {
            Speed = 4.5f,
            Lives = 3,
            Direction = new Vector3(1, 2, 3),
            Target = target,
            Multiplier = 2.0f,
            Enabled = false,
        });

        Scene loaded = RoundTrip(scene);

        var restored = FindByName(loaded, "Mover").GetComponent<SerializedMover>();
        Assert.Equal(4.5f, restored.Speed);
        Assert.Equal(3, restored.Lives);
        Assert.Equal(new Vector3(1, 2, 3), restored.Direction);
        Assert.Equal(2.0f, restored.Multiplier);
        Assert.False(restored.Enabled);
        Assert.Equal(FindByName(loaded, "Target"), restored.Target);
        Assert.Equal(FindByName(loaded, "Mover"), restored.Entity);
    }

    [Fact]
    public void UserComponents_AreWrittenInOrder_WithoutTheBaseComponentState()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        entity.AddComponent<SerializedTagAlong>();
        entity.AddComponent<SerializedMover>();

        JsonObject entityObj = WrittenEntity(scene);

        var items = entityObj["Components"]!.AsArray();
        Assert.Equal(new[] { nameof(SerializedTagAlong), nameof(SerializedMover) }, items.Select(i => (string)i!["Type"]!));
        Assert.Null(items[0]!["Fields"]); // no tunables -> no Fields block
        Assert.Null(items[1]!["Fields"]!["Enabled"]); // Enabled lives beside the fields, not among them

        Scene loaded = RoundTrip(scene);
        Assert.Equal(
            new[] { typeof(SerializedTagAlong), typeof(SerializedMover) },
            FindByName(loaded, "A").Components.Where(c => c.IsUserComponent).Select(c => c.GetType()));
    }

    [Fact]
    public void EntityWithoutUserComponents_WritesNoComponentsBlock()
    {
        var scene = new Scene();
        scene.Instantiate("A");

        Assert.Null(WrittenEntity(scene)["Components"]);
    }

    [Fact]
    public void UnresolvableComponent_IsKeptVerbatim_AndWrittenBackUnchanged()
    {
        const string json = """
            { "Entities": [ { "Tag": { "Name": "A", "Id": "a1" },
              "Components": [
                { "Type": "SerializedTagAlong" },
                { "Type": "NoSuchComponentType", "Guid": "nope", "Enabled": false, "Fields": { "Speed": 7, "Target": "a1" } }
              ] } ] }
            """;

        var scene = new Scene();
        Assert.True(new SceneSerializer(scene).DeserializeFromString(json));

        Entity entity = FindByName(scene, "A");
        Assert.True(entity.HasComponent<SerializedTagAlong>());
        MissingComponent missing = Assert.Single(entity.GetComponent<MissingComponents>().Items);
        Assert.Equal("NoSuchComponentType", missing.TypeName);
        Assert.Equal("nope", missing.Guid);

        var written = WrittenEntity(scene)["Components"]!.AsArray();
        Assert.Equal(2, written.Count);
        Assert.True(JsonNode.DeepEquals(missing.Data, written[1]));
    }

    [Fact]
    public void MissingComponent_ResolvesByGuid_OnceItsTypeIsRegistered()
    {
        const string json = """
            { "Entities": [ { "Tag": { "Name": "A", "Id": "a1" },
              "Components": [ { "Type": "RenamedAway", "Guid": "late-component-guid", "Fields": { "Value": 9 } } ] } ] }
            """;

        var scene = new Scene();
        Assert.True(new SceneSerializer(scene).DeserializeFromString(json));
        Entity entity = FindByName(scene, "A");
        Assert.True(entity.HasComponent<MissingComponents>());

        var provider = new GuidProvider();
        ScriptRegistry.Register(provider);
        try
        {
            Assert.Equal(1, SceneSerializer.ResolveMissingComponents(scene));
        }
        finally
        {
            ScriptRegistry.Unregister(provider);
        }

        Assert.Equal(9, entity.GetComponent<GuidOnlyComponent>().Value);
        Assert.False(entity.HasComponent<MissingComponents>());
    }

    [Fact]
    public void RegisteredComponent_IsWrittenWithItsGuid()
    {
        var provider = new GuidProvider();
        ScriptRegistry.Register(provider);
        try
        {
            var scene = new Scene();
            scene.Instantiate("A").AddComponent(new GuidOnlyComponent { Value = 1 });

            var entry = WrittenEntity(scene)["Components"]!.AsArray().Single()!;
            Assert.Equal(LateGuid, (string)entry["Guid"]!);
        }
        finally
        {
            ScriptRegistry.Unregister(provider);
        }
    }

    private static JsonObject WrittenEntity(Scene scene)
    {
        JsonNode root = JsonNode.Parse(new SceneSerializer(scene).SerializeToString())!;
        return root["Entities"]!.AsArray().Single()!.AsObject();
    }
}
