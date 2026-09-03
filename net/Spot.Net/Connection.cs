namespace Spot.Net;

/// <summary>
/// A handle to one peer on the other end of the wire: on a server, a connected client; on a client, the
/// server. Held by <see cref="NetworkManager"/> and surfaced through its connection events.
/// </summary>
public sealed class Connection
{
    internal Connection(int id)
    {
        Id = id;
    }

    /// <summary>
    /// The transport-level id of this connection. On a server each client has a distinct positive id; on
    /// a client the connection to the server is <c>0</c>.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// An optional application tag for this connection (for example the network id of the client's player
    /// object). The engine leaves this null; games may use it to associate a connection with game state.
    /// </summary>
    public object? UserData { get; set; }
}
