using System.Linq;
using System.Numerics;
using Spot.Net;
using Spot.Scenes;

namespace Spot.Net.Tests;

/// <summary>
/// P2 checks over the real loopback transport: a server RPC travels client→server, a client RPC travels
/// server→client, and a <see cref="SyncVar{T}"/> set on the server propagates to the client.
/// </summary>
[Collection("network-loopback")]
public class RpcTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    static RpcTests()
    {
        NetTest.EnsureLogging();
        NetworkPrefabs.Register("RpcPlayer", scene =>
        {
            Entity e = scene.Instantiate("RpcPlayer");
            e.AddComponent(new NetworkObject());
            e.AddComponent(new NetworkTransform());
            e.AddScript<RpcProbe>();
            return e;
        });
    }

    [Fact]
    public void ServerRpc_Runs_On_Server_When_Owner_Invokes()
    {
        Run((server, client, clientPlayerId) =>
        {
            Probe(client.Replication.Objects[clientPlayerId]).DoFire(new Vector3(1f, 2f, 3f));

            Assert.True(
                NetTest.PumpUntil(() => Probe(server.Replication.Objects[clientPlayerId]).ServerRpcCalls == 1, Timeout, server, client),
                "server RPC did not run on the server");
            Assert.Equal(new Vector3(1f, 2f, 3f), Probe(server.Replication.Objects[clientPlayerId]).LastOrigin);
        });
    }

    [Fact]
    public void ClientRpc_Runs_On_Client_When_Server_Invokes()
    {
        Run((server, client, clientPlayerId) =>
        {
            Probe(server.Replication.Objects[clientPlayerId]).DoEffect(42.5f);

            Assert.True(
                NetTest.PumpUntil(() => Probe(client.Replication.Objects[clientPlayerId]).ClientRpcCalls == 1, Timeout, server, client),
                "client RPC did not run on the client");
            Assert.Equal(42.5f, Probe(client.Replication.Objects[clientPlayerId]).LastValue);
        });
    }

    [Fact]
    public void SyncVar_Set_On_Server_Propagates_To_Client()
    {
        Run((server, client, clientPlayerId) =>
        {
            Probe(server.Replication.Objects[clientPlayerId]).Health.Value = 75;

            Assert.True(
                NetTest.PumpUntil(() => Probe(client.Replication.Objects[clientPlayerId]).Health.Value == 75, Timeout, server, client),
                "sync var did not propagate to the client");
        });
    }

    // Boots a host + client, waits for the client's server-spawned player to exist on both peers, then runs
    // the test body with that object's id.
    private static void Run(Action<NetworkManager, NetworkManager, uint> body)
    {
        int port = NetTest.GetFreePort();
        var server = new NetworkManager { Scene = new Scene(), PlayerPrefab = "RpcPlayer" };
        var client = new NetworkManager { Scene = new Scene() };

        try
        {
            server.StartHost(port);
            client.StartClient("localhost", port);
            Assert.True(NetTest.PumpUntil(() => client.LocalConnectionId >= 0, Timeout, server, client), "did not connect");

            Assert.True(
                NetTest.PumpUntil(
                    () => TryFindOwned(client, client.LocalConnectionId, out uint id) && server.Replication.Objects.ContainsKey(id),
                    Timeout, server, client),
                "the client's player did not replicate to both peers");

            Assert.True(TryFindOwned(client, client.LocalConnectionId, out uint playerId));
            body(server, client, playerId);
        }
        finally
        {
            client.Stop();
            server.Stop();
        }
    }

    private static bool TryFindOwned(NetworkManager manager, int ownerId, out uint id)
    {
        foreach (KeyValuePair<uint, Entity> kv in manager.Replication.Objects)
        {
            if (kv.Value.IsValid && kv.Value.TryGetComponent(out NetworkObject? obj) && obj.OwnerId == ownerId)
            {
                id = kv.Key;
                return true;
            }
        }

        id = 0;
        return false;
    }

    private static RpcProbe Probe(Entity entity) =>
        entity.GetComponent<ScriptComponent>().Items.Select(i => i.Instance).OfType<RpcProbe>().First();

    private sealed class RpcProbe : NetworkBehaviour
    {
        public int ServerRpcCalls;
        public Vector3 LastOrigin;
        public int ClientRpcCalls;
        public float LastValue;
        public readonly SyncVar<int> Health;

        public RpcProbe() => Health = Sync<int>(100);

        public void DoFire(Vector3 origin) => InvokeServerRpc(nameof(Fire), origin);

        public void DoEffect(float value) => InvokeClientRpc(nameof(Effect), value);

        [ServerRpc]
        private void Fire(Vector3 origin)
        {
            ServerRpcCalls++;
            LastOrigin = origin;
        }

        [ClientRpc]
        private void Effect(float value)
        {
            ClientRpcCalls++;
            LastValue = value;
        }
    }
}
