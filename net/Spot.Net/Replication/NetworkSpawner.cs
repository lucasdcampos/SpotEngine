using Spot.Scenes;

namespace Spot.Net;

/// <summary>
/// The ergonomic front door to networked spawning, over the shared <see cref="NetworkManager.Instance"/>.
/// Register prefabs on every peer at startup; call the server-authoritative spawn/despawn from server code.
/// </summary>
public static class NetworkSpawner
{
    /// <summary>Registers a networked prefab factory under a key (see <see cref="NetworkPrefabs.Register"/>).</summary>
    /// <param name="key">The prefab key referenced by spawns.</param>
    /// <param name="factory">Builds the entity in a given scene.</param>
    public static void RegisterPrefab(string key, Func<Scene, Entity> factory) => NetworkPrefabs.Register(key, factory);

    /// <summary>
    /// Server-only: spawns a registered prefab as a networked object owned by <paramref name="ownerId"/>
    /// (<c>-1</c> = the server), replicating it to every client.
    /// </summary>
    /// <param name="prefabKey">The registered prefab key.</param>
    /// <param name="ownerId">The owning connection id, or <c>-1</c> for the server.</param>
    /// <returns>The spawned entity, or <see langword="null"/> if spawning failed.</returns>
    public static Entity? ServerSpawn(string prefabKey, int ownerId = -1) =>
        NetworkManager.Instance.ServerSpawn(prefabKey, ownerId);

    /// <summary>Server-only: despawns a networked object everywhere.</summary>
    /// <param name="entity">The networked entity to despawn.</param>
    public static void ServerDespawn(Entity entity) => NetworkManager.Instance.ServerDespawn(entity);
}
