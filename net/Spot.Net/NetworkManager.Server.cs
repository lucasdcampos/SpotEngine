namespace Spot.Net;

/// <summary>
/// Desktop-only half of <see cref="NetworkManager"/>: installs the WebSocket <em>server</em> transport,
/// which is built on <c>HttpListener</c> and therefore cannot run in a browser. This file is excluded
/// from the wasm target (see Spot.Net.csproj), so on that platform the partial method below is never
/// compiled and hosting reports itself as unsupported.
/// </summary>
public sealed partial class NetworkManager
{
    partial void InstallServerTransport() => _transport = new WebSocketServerTransport();
}
