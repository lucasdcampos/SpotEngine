namespace Spot.Net;

/// <summary>
/// The leading byte of every engine network message, identifying how to parse the rest. Application
/// messages sent through <c>NetworkManager.SendTo*</c> are wrapped in <see cref="Raw"/> so user traffic
/// never collides with the engine's own replication messages on the same connection.
/// </summary>
public enum MessageType : byte
{
    /// <summary>Application payload sent via <c>NetworkManager.SendToServer/Client/All</c>.</summary>
    Raw = 0,

    /// <summary>Server → client: assigns the client its identity right after connecting.</summary>
    Welcome = 1,

    /// <summary>Server → client: spawn a networked object (id, owner, prefab key, initial transform).</summary>
    Spawn = 2,

    /// <summary>Server → client: despawn a networked object by id.</summary>
    Despawn = 3,

    /// <summary>Server → client: a world-state snapshot of replicated transforms.</summary>
    StateSnapshot = 4,

    /// <summary>A remote procedure call routed to a networked object's behaviour.</summary>
    Rpc = 5,

    /// <summary>A synchronized-variable update for a networked object's behaviour.</summary>
    SyncVar = 6,
}
