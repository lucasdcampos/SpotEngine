namespace Spot.Net;

/// <summary>
/// The low-level networking seam: moves opaque byte messages between peers and reports connects,
/// disconnects and inbound data. Everything above it (identity, spawning, replication, RPCs) is
/// transport-agnostic, so swapping the wire (WebSocket today; UDP/WebRTC later) needs no changes there.
/// </summary>
/// <remarks>
/// A transport does its I/O in the background and queues results; <see cref="Poll"/> drains that queue on
/// the game thread so callers never touch sockets or threads directly. Implementations must never throw
/// out to the caller — a failed send or a hostile packet is logged and surfaced as a disconnect, honoring
/// the engine's "never crash" rule.
/// </remarks>
public interface ITransport
{
    /// <summary>Whether the transport is currently listening or connected.</summary>
    bool IsRunning { get; }

    /// <summary>Starts listening for incoming client connections (server role).</summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="maxConnections">The maximum number of simultaneous clients to accept.</param>
    void StartServer(int port, int maxConnections);

    /// <summary>Connects to a remote server (client role).</summary>
    /// <param name="address">The server host name or IP.</param>
    /// <param name="port">The server port.</param>
    void StartClient(string address, int port);

    /// <summary>
    /// Drains all events that have arrived since the last call, invoking <paramref name="onEvent"/> for
    /// each on the calling (game) thread, in arrival order.
    /// </summary>
    /// <param name="onEvent">The handler for each queued event.</param>
    void Poll(Action<TransportEvent> onEvent);

    /// <summary>Sends a message to a single connection. A no-op if the connection is unknown or closed.</summary>
    /// <param name="connectionId">The target connection id.</param>
    /// <param name="data">The message bytes.</param>
    /// <param name="method">The requested delivery guarantee.</param>
    void Send(int connectionId, ReadOnlySpan<byte> data, DeliveryMethod method);

    /// <summary>Sends a message to every open connection.</summary>
    /// <param name="data">The message bytes.</param>
    /// <param name="method">The requested delivery guarantee.</param>
    void Broadcast(ReadOnlySpan<byte> data, DeliveryMethod method);

    /// <summary>Closes a single connection. A no-op if it is unknown or already closed.</summary>
    /// <param name="connectionId">The connection to close.</param>
    void Disconnect(int connectionId);

    /// <summary>Stops the transport, closing all connections and releasing resources.</summary>
    void Stop();
}
