namespace Spot.Net;

/// <summary>
/// Execution-order slots for the networking scene systems, chosen relative to the engine's built-in
/// systems (character controllers 100, physics 200/300, scripts 700). Inbound state is applied before
/// everything so scripts and physics see the up-to-date world this frame; outbound state is sent after
/// scripts so it reflects the frame's resolved result.
/// </summary>
public static class NetworkSystemOrder
{
    /// <summary>Poll the transport and apply inbound state, before any built-in system.</summary>
    public const int Receive = 40;

    /// <summary>Send outbound state, after user scripts (<c>SystemOrder.Scripts</c> = 700) have run.</summary>
    public const int Send = 750;
}
