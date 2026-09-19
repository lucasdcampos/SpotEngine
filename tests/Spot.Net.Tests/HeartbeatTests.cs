using Spot.Net;

namespace Spot.Net.Tests;

/// <summary>
/// Connection-health checks: heartbeats keep a quiet connection alive, and a peer that goes silent past the
/// timeout is dropped even though its socket never closed cleanly.
/// </summary>
[Collection("network-loopback")]
public class HeartbeatTests
{
    static HeartbeatTests() => NetTest.EnsureLogging();

    [Fact]
    public void Silent_Client_Times_Out_On_Server()
    {
        float savedTimeout = NetworkSettings.TimeoutSeconds;
        float savedHeartbeat = NetworkSettings.HeartbeatIntervalSeconds;
        NetworkSettings.TimeoutSeconds = 0.5f;
        NetworkSettings.HeartbeatIntervalSeconds = 0.05f;

        int port = NetTest.GetFreePort();
        var server = new NetworkManager();
        var client = new NetworkManager();

        Connection? connected = null;
        bool disconnected = false;
        server.ClientConnected += c => connected = c;
        server.ClientDisconnected += _ => disconnected = true;

        try
        {
            server.StartHost(port);
            client.StartClient("localhost", port);
            Assert.True(NetTest.PumpUntil(() => connected is not null, TimeSpan.FromSeconds(5), server, client), "client never connected");

            // Stop pumping the client entirely: its socket stays open but it sends nothing, so the server
            // must notice the silence and drop it. Only the server is pumped now.
            Assert.True(
                NetTest.PumpUntil(() => disconnected, TimeSpan.FromSeconds(5), server),
                "server did not time out the silent client");
        }
        finally
        {
            client.Stop();
            server.Stop();
            NetworkSettings.TimeoutSeconds = savedTimeout;
            NetworkSettings.HeartbeatIntervalSeconds = savedHeartbeat;
        }
    }

    [Fact]
    public void Heartbeat_Keeps_A_Quiet_Connection_Alive()
    {
        float savedTimeout = NetworkSettings.TimeoutSeconds;
        float savedHeartbeat = NetworkSettings.HeartbeatIntervalSeconds;
        NetworkSettings.TimeoutSeconds = 0.4f;
        NetworkSettings.HeartbeatIntervalSeconds = 0.05f;

        int port = NetTest.GetFreePort();
        var server = new NetworkManager();
        var client = new NetworkManager();

        Connection? connected = null;
        bool disconnected = false;
        server.ClientConnected += c => connected = c;
        server.ClientDisconnected += _ => disconnected = true;

        try
        {
            server.StartHost(port);
            client.StartClient("localhost", port);
            Assert.True(NetTest.PumpUntil(() => connected is not null, TimeSpan.FromSeconds(5), server, client), "client never connected");

            // Both peers keep pumping but send no gameplay traffic; heartbeats alone must hold the
            // connection open well past the timeout window.
            NetTest.Pump(150, server, client);
            Assert.False(disconnected, "a heartbeated connection was wrongly timed out");
            Assert.Contains(server.Connections, c => c.Id > 0);
        }
        finally
        {
            client.Stop();
            server.Stop();
            NetworkSettings.TimeoutSeconds = savedTimeout;
            NetworkSettings.HeartbeatIntervalSeconds = savedHeartbeat;
        }
    }
}
