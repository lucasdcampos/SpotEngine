using System.Numerics;
using Spot.Net;
using Spot.Scenes;

namespace Spot.Net.Tests;

/// <summary>
/// P1 checks over the real loopback transport: server-authoritative spawn/despawn replicates to a client,
/// and a replicated transform converges on the client through interpolation.
/// </summary>
[Collection("network-loopback")]
public class ReplicationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    static ReplicationTests()
    {
        NetTest.EnsureLogging();
        NetworkPrefabs.Register("Cube", scene =>
        {
            Entity e = scene.Instantiate("Cube");
            e.AddComponent(new NetworkObject());
            e.AddComponent(new NetworkTransform());
            return e;
        });
    }

    [Fact]
    public void ServerSpawn_Replicates_Object_To_Client()
    {
        int port = NetTest.GetFreePort();
        var server = new NetworkManager { Scene = new Scene() };
        var client = new NetworkManager { Scene = new Scene() };

        try
        {
            server.StartHost(port);
            client.StartClient("localhost", port);
            Assert.True(NetTest.PumpUntil(() => client.LocalConnectionId >= 0, Timeout, server, client), "did not connect");

            Entity spawned = server.ServerSpawn("Cube", ownerId: -1)!.Value;
            uint id = spawned.GetComponent<NetworkObject>().NetworkId.Value;

            Assert.True(
                NetTest.PumpUntil(() => client.Replication.Objects.ContainsKey(id), Timeout, server, client),
                "spawn did not replicate to the client");

            Entity clientCopy = client.Replication.Objects[id];
            Assert.Equal(id, clientCopy.GetComponent<NetworkObject>().NetworkId.Value);
            Assert.Equal(-1, clientCopy.GetComponent<NetworkObject>().OwnerId);
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }

    [Fact]
    public void Replicated_Transform_Converges_On_Client()
    {
        int port = NetTest.GetFreePort();
        var server = new NetworkManager { Scene = new Scene() };
        var client = new NetworkManager { Scene = new Scene() };

        try
        {
            server.StartHost(port);
            client.StartClient("localhost", port);
            Assert.True(NetTest.PumpUntil(() => client.LocalConnectionId >= 0, Timeout, server, client), "did not connect");

            Entity spawned = server.ServerSpawn("Cube", ownerId: -1)!.Value;
            uint id = spawned.GetComponent<NetworkObject>().NetworkId.Value;
            Assert.True(NetTest.PumpUntil(() => client.Replication.Objects.ContainsKey(id), Timeout, server, client), "spawn did not replicate");

            var target = new Vector3(5f, 1f, -2f);
            spawned.GetComponent<TransformComponent>().Position = target;

            Entity clientCopy = client.Replication.Objects[id];
            Assert.True(
                NetTest.PumpUntil(
                    () => Vector3.Distance(clientCopy.GetComponent<TransformComponent>().Position, target) < 0.1f,
                    Timeout, server, client),
                "client transform did not converge on the server value");
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }

    [Fact]
    public void ServerDespawn_Removes_Object_On_Client()
    {
        int port = NetTest.GetFreePort();
        var server = new NetworkManager { Scene = new Scene() };
        var client = new NetworkManager { Scene = new Scene() };

        try
        {
            server.StartHost(port);
            client.StartClient("localhost", port);
            Assert.True(NetTest.PumpUntil(() => client.LocalConnectionId >= 0, Timeout, server, client), "did not connect");

            Entity spawned = server.ServerSpawn("Cube", ownerId: -1)!.Value;
            uint id = spawned.GetComponent<NetworkObject>().NetworkId.Value;
            Assert.True(NetTest.PumpUntil(() => client.Replication.Objects.ContainsKey(id), Timeout, server, client), "spawn did not replicate");

            server.ServerDespawn(spawned);
            Assert.True(
                NetTest.PumpUntil(() => !client.Replication.Objects.ContainsKey(id), Timeout, server, client),
                "despawn did not remove the object on the client");
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }
}
