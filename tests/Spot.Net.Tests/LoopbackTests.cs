using System.Text;
using Spot.Net;

namespace Spot.Net.Tests;

/// <summary>
/// End-to-end P0 checks: a host and a client, each its own <see cref="NetworkManager"/> over the real
/// WebSocket transport on the loopback interface, connect and exchange raw application messages both ways.
/// </summary>
[Collection("network-loopback")]
public class LoopbackTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    static LoopbackTests() => NetTest.EnsureLogging();

    [Fact]
    public void HostAndClient_Connect_And_ExchangeRawMessages_BothWays()
    {
        int port = NetTest.GetFreePort();
        var server = new NetworkManager();
        var client = new NetworkManager();

        Connection? serverSawClient = null;
        bool clientConnected = false;
        server.ClientConnected += c => serverSawClient = c;
        client.ConnectedToServer += () => clientConnected = true;

        string? serverReceived = null;
        int serverReceivedFrom = -99;
        server.MessageReceived += (from, reader) =>
        {
            serverReceivedFrom = from;
            serverReceived = ReadUtf8(reader);
        };

        string? clientReceived = null;
        client.MessageReceived += (_, reader) => clientReceived = ReadUtf8(reader);

        try
        {
            server.StartHost(port);
            client.StartClient("localhost", port);

            Assert.True(
                NetTest.PumpUntil(
                    () => clientConnected && serverSawClient is not null && client.LocalConnectionId >= 0,
                    Timeout, server, client),
                "client and server did not establish a connection in time");

            // The server learns the client through a positive connection id; the client learns its own id
            // from the welcome message.
            Assert.NotNull(serverSawClient);
            Assert.True(serverSawClient!.Id > 0);
            Assert.Equal(serverSawClient.Id, client.LocalConnectionId);

            // Client -> server.
            client.SendToServer(Utf8("ping from client"));
            Assert.True(NetTest.PumpUntil(() => serverReceived is not null, Timeout, server, client), "server did not receive the client message");
            Assert.Equal("ping from client", serverReceived);
            Assert.Equal(serverSawClient.Id, serverReceivedFrom);

            // Server -> that client.
            server.SendToClient(serverSawClient.Id, Utf8("pong from server"));
            Assert.True(NetTest.PumpUntil(() => clientReceived is not null, Timeout, server, client), "client did not receive the server message");
            Assert.Equal("pong from server", clientReceived);
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }

    [Fact]
    public void Client_Disconnect_Raises_ClientDisconnected_OnServer()
    {
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
            Assert.True(NetTest.PumpUntil(() => connected is not null, Timeout, server, client), "client never connected");

            client.Stop();
            Assert.True(NetTest.PumpUntil(() => disconnected, Timeout, server, client), "server never observed the disconnect");
            Assert.DoesNotContain(server.Connections, c => c.Id > 0);
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static string ReadUtf8(NetReader reader)
    {
        var bytes = new List<byte>();
        while (reader.Remaining > 0)
        {
            bytes.Add(reader.ReadByte());
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
