using Spot.Console;
using Spot.Core;

namespace Spot.Net;

/// <summary>
/// Registers developer-console commands (<c>net_host</c>, <c>net_connect</c>, <c>net_stop</c>,
/// <c>net_status</c>) that drive the <see cref="NetworkManager"/>. Desktop-only, because the engine's
/// developer console is itself compiled out of the browser build. Call <see cref="Install"/> once after the
/// application has started; the programmatic <see cref="NetworkManager"/> API is the platform-neutral path.
/// </summary>
public static class NetworkConsole
{
    /// <summary>Registers the networking commands with the given console (or the running app's console).</summary>
    /// <param name="console">The console to register with; defaults to <c>Application.Instance.Console</c>.</param>
    public static void Install(DevConsole? console = null)
    {
        console ??= Application.Instance.Console;

        console.Register("net_host", args =>
        {
            int port = ParsePort(args, 0) ?? NetworkSettings.Port;
            NetworkManager.Instance.StartHost(port);
            console.Print($"Hosting on port {port} (role: {NetworkManager.Instance.Role}).");
            if (NetworkSettings.BindAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                console.Print("Hint: bound to localhost — only same-machine clients can connect. Set NetworkSettings.BindAddress = \"+\" to accept remote clients.");
        }, "Starts a listen server: 'net_host [port]'");

        console.Register("net_connect", args =>
        {
            string address = args.Count > 0 ? args[0] : NetworkSettings.DefaultAddress;
            int port = ParsePort(args, 1) ?? NetworkSettings.Port;
            NetworkManager.Instance.StartClient(address, port);
            console.Print($"Connecting to {address}:{port}…");
        }, "Connects to a server as a client: 'net_connect [address] [port]'");

        console.Register("net_stop", _ =>
        {
            NetworkManager.Instance.Stop();
            console.Print("Networking stopped.");
        }, "Ends the current networking session");

        console.Register("net_status", _ =>
        {
            NetworkManager net = NetworkManager.Instance;
            console.Print($"Role: {net.Role}  LocalId: {net.LocalConnectionId}  Tick: {NetworkSettings.TickRate} Hz");
            if (net.IsServer)
                console.Print($"Clients connected: {net.Connections.Count}");
            console.Print($"Networked objects: {net.Replication.Objects.Count}");
        }, "Prints the current networking role, id, connection count and object count");
    }

    private static int? ParsePort(IReadOnlyList<string> args, int index)
    {
        if (args.Count > index && int.TryParse(args[index], out int port))
        {
            return port;
        }

        return null;
    }
}
