namespace Spot.Net;

/// <summary>
/// Global, engine-wide knobs for networking, following the same static-settings convention as
/// <c>PhysicsSettings</c> and <c>RenderSettings</c>. Values are read when a session starts (address/port)
/// or each frame (tick rate, interpolation), so changing them affects the next session or the next tick.
/// </summary>
public static class NetworkSettings
{
    /// <summary>
    /// How many times per second the server sends world-state snapshots (and clients flush their
    /// outbound state). Higher is smoother but uses more bandwidth. Reliable traffic (spawns, RPCs) is
    /// sent immediately and does not wait for the tick. Defaults to 30.
    /// </summary>
    public static int TickRate { get; set; } = 30;

    /// <summary>The default port used by <c>StartHost</c>/<c>StartServer</c> and <c>StartClient</c> when none is given. Defaults to 7777.</summary>
    public static int Port { get; set; } = 7777;

    /// <summary>The default server address used by <c>StartClient</c> when none is given. Defaults to <c>127.0.0.1</c>.</summary>
    public static string DefaultAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// The host the server binds to. <c>localhost</c> (the default) needs no special OS permission and
    /// accepts same-machine clients (including a browser tab). To accept clients from other machines, set
    /// this to <c>+</c> or a specific address — which on Windows may require a URL reservation (see docs).
    /// </summary>
    public static string BindAddress { get; set; } = "localhost";

    /// <summary>The maximum number of simultaneous clients a server accepts. Defaults to 16.</summary>
    public static int MaxConnections { get; set; } = 16;

    /// <summary>
    /// How often, in seconds, each peer sends a heartbeat so the other end can tell a live-but-quiet
    /// connection from a dead one. Should be well below <see cref="TimeoutSeconds"/>. Defaults to 2.
    /// </summary>
    public static float HeartbeatIntervalSeconds { get; set; } = 2f;

    /// <summary>
    /// How long, in seconds, a connection may go without any inbound message before it is considered dead
    /// and dropped. Set to 0 to disable timeouts entirely. Defaults to 10.
    /// </summary>
    public static float TimeoutSeconds { get; set; } = 10f;

    /// <summary>
    /// The largest single message the transport will accept, in bytes. A peer that sends more is dropped,
    /// bounding the memory a hostile or buggy client can make the server buffer. Defaults to 64 KiB.
    /// </summary>
    public static int MaxMessageBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// How far behind real time a client renders replicated transforms, in seconds, so it always has two
    /// snapshots to interpolate between and hides jitter. Defaults to 0.1 (100 ms).
    /// </summary>
    public static float InterpolationDelaySeconds { get; set; } = 0.1f;

    /// <summary>The tick interval in seconds, derived from <see cref="TickRate"/> (clamped to a sane range).</summary>
    public static float TickInterval => 1f / Math.Clamp(TickRate, 1, 240);
}
