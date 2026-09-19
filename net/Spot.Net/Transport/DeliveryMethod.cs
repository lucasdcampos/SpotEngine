namespace Spot.Net;

/// <summary>
/// How a message should be delivered by the transport.
/// </summary>
/// <remarks>
/// The default WebSocket transport runs over TCP, so it delivers <em>everything</em> reliably and in
/// order; <see cref="Unreliable"/> is treated the same as <see cref="Reliable"/> there. The distinction
/// is kept in the API so gameplay code can express intent and so a future UDP/WebRTC transport can honor
/// it without an API change.
/// </remarks>
public enum DeliveryMethod
{
    /// <summary>Best-effort delivery: may be dropped or reordered (on transports that support it).</summary>
    Unreliable,

    /// <summary>Guaranteed, in-order delivery.</summary>
    Reliable,
}
