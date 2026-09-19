using Spot.Scenes;

namespace Spot.Net;

/// <summary>
/// The global registry of networked prefabs: a factory per string key that builds the entity (mesh,
/// components, scripts) for a networked object. The server spawns by key and every peer recreates the same
/// object from its own factory, so the key — not a scene reference — is what travels on the wire.
/// </summary>
public static class NetworkPrefabs
{
    private static readonly Dictionary<string, Func<Scene, Entity>> s_factories = new();

    /// <summary>
    /// Registers a prefab factory under <paramref name="key"/>, replacing any existing one. Call this at
    /// startup on every peer (server and clients) so the key resolves the same way everywhere.
    /// </summary>
    /// <param name="key">The prefab key referenced by spawns.</param>
    /// <param name="factory">Builds the entity in a given scene.</param>
    public static void Register(string key, Func<Scene, Entity> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);
        s_factories[key] = factory;
    }

    /// <summary>Looks up a registered prefab factory.</summary>
    /// <param name="key">The prefab key.</param>
    /// <param name="factory">The factory, if registered.</param>
    /// <returns>Whether a factory was found.</returns>
    public static bool TryGet(string key, out Func<Scene, Entity> factory) => s_factories.TryGetValue(key, out factory!);

    /// <summary>Removes all registered prefabs.</summary>
    public static void Clear() => s_factories.Clear();
}
