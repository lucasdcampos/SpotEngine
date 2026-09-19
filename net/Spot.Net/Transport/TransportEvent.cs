namespace Spot.Net;

/// <summary>
/// The kind of a <see cref="TransportEvent"/> drained from a transport during <see cref="ITransport.Poll"/>.
/// </summary>
public enum TransportEventKind
{
    /// <summary>A new connection was established (a client on the server, or the server on a client).</summary>
    Connected,

    /// <summary>A connection was closed or lost.</summary>
    Disconnected,

    /// <summary>A message arrived on a connection; see <see cref="TransportEvent.Payload"/>.</summary>
    Data,
}

/// <summary>
/// A single transport-level occurrence: a connect, a disconnect, or an inbound message. Transports queue
/// these off their background I/O and hand them back on the game thread through <see cref="ITransport.Poll"/>.
/// </summary>
public readonly struct TransportEvent
{
    /// <summary>Creates a transport event.</summary>
    /// <param name="kind">The kind of event.</param>
    /// <param name="connectionId">The connection it concerns.</param>
    /// <param name="payload">The message bytes for <see cref="TransportEventKind.Data"/>; otherwise empty.</param>
    public TransportEvent(TransportEventKind kind, int connectionId, ReadOnlyMemory<byte> payload)
    {
        Kind = kind;
        ConnectionId = connectionId;
        Payload = payload;
    }

    /// <summary>The kind of event.</summary>
    public TransportEventKind Kind { get; }

    /// <summary>
    /// The connection this event concerns. On a client, the single connection to the server is
    /// <c>0</c>; on a server, each client gets a distinct positive id.
    /// </summary>
    public int ConnectionId { get; }

    /// <summary>The message bytes for a <see cref="TransportEventKind.Data"/> event; empty otherwise.</summary>
    public ReadOnlyMemory<byte> Payload { get; }
}
