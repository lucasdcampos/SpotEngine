using Spot.Scenes;

namespace Spot.Net;

/// <summary>
/// Marks an entity as a networked object with a session-wide <see cref="NetworkId"/> and an owner. The
/// server assigns the id and owner when the object is spawned (see <see cref="NetworkSpawner"/>); every
/// peer that replicates the object carries a matching <see cref="NetworkObject"/>.
/// </summary>
public sealed class NetworkObject : Component
{
    /// <summary>The object's session-wide id, assigned by the server on spawn.</summary>
    public NetworkId NetworkId { get; internal set; }

    /// <summary>
    /// The connection id that owns this object: a client's id for its player, or <c>-1</c> for objects the
    /// server owns. Ownership decides who reads local input and drives the object's transform.
    /// </summary>
    public int OwnerId { get; internal set; } = -1;

    /// <summary>The prefab key this object was spawned from, so remote peers can recreate it.</summary>
    public string PrefabKey { get; internal set; } = string.Empty;

    /// <summary>
    /// Whether the local peer owns this object (its owner equals this peer's connection id). Uses the shared
    /// <see cref="NetworkManager.Instance"/>; the owning peer reads input and moves the object, others
    /// replicate it.
    /// </summary>
    public bool IsOwner => NetworkManager.Instance.LocalConnectionId == OwnerId;
}
