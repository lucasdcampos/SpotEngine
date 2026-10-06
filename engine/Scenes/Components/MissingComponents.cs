using Spot.Engine.Scenes;
using System.Text.Json.Nodes;

namespace Spot.Engine;

/// <summary>
/// Keeps the user components of an entity whose type could not be resolved when the scene was loaded — a
/// script that has not been compiled yet, was deleted, or failed to build. Each entry holds the component's
/// authored data exactly as it was read, and the scene serializer writes it back unchanged, so opening and
/// saving a scene never loses a component just because its code is momentarily unavailable. The entries are
/// resolved into real components once the type becomes available (for example after the editor loads the
/// game's scripts).
/// </summary>
public sealed class MissingComponents : Component
{
    /// <summary>Gets the unresolved entries, in the order they were read.</summary>
    public List<MissingComponent> Items { get; } = new();
}

/// <summary>
/// One user component that could not be resolved: its authored scene data (type name, guid, enabled state and
/// field values), preserved verbatim.
/// </summary>
public sealed class MissingComponent
{
    /// <summary>Initializes an entry from the component's scene data.</summary>
    /// <param name="data">The component's JSON object as stored in the scene.</param>
    public MissingComponent(JsonObject data)
    {
        Data = data;
    }

    /// <summary>Gets the component's scene data, written back unchanged when the scene is saved.</summary>
    public JsonObject Data { get; }

    /// <summary>Gets the component's class name as authored (empty when absent).</summary>
    public string TypeName => Data["Type"] is JsonValue value && value.TryGetValue(out string? name) ? name : string.Empty;

    /// <summary>Gets the component's stable script guid (empty when absent).</summary>
    public string Guid => Data["Guid"] is JsonValue value && value.TryGetValue(out string? guid) ? guid : string.Empty;
}
