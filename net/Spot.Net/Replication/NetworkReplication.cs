using System.Numerics;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Net;

/// <summary>
/// The replication engine behind <see cref="NetworkManager"/>: it owns the live-object registry, performs
/// server-authoritative spawn/despawn, sends and applies transform snapshots, and runs the client-side
/// interpolation. It is driven by the manager's connection events and network tick, and reads/writes the
/// manager's <see cref="NetworkManager.Scene"/>. All inbound data is treated as untrusted — malformed
/// messages degrade to no-ops rather than throwing.
/// </summary>
internal sealed class NetworkReplication
{
    private readonly NetworkManager _net;
    private readonly Dictionary<uint, Entity> _objects = new();
    private uint _nextId;

    public NetworkReplication(NetworkManager net) => _net = net;

    /// <summary>The live networked objects on this peer, keyed by network id.</summary>
    public IReadOnlyDictionary<uint, Entity> Objects => _objects;

    /// <summary>Server-only: spawns a prefab as a networked object and replicates it to all clients.</summary>
    public Entity? ServerSpawn(string prefabKey, int ownerId)
    {
        if (!_net.IsServer)
        {
            Log.CoreWarn("Spot.Net: ServerSpawn is server-only.");
            return null;
        }

        Scene? scene = _net.Scene;
        if (scene is null)
        {
            Log.CoreWarn("Spot.Net: ServerSpawn has no scene to spawn into.");
            return null;
        }

        if (!NetworkPrefabs.TryGet(prefabKey, out Func<Scene, Entity> factory))
        {
            Log.CoreError("Spot.Net: no networked prefab registered under key '{0}'.", prefabKey);
            return null;
        }

        Entity entity = factory(scene);
        NetworkObject obj = entity.TryGetComponent(out NetworkObject? existing) ? existing : entity.AddComponent(new NetworkObject());

        uint id = ++_nextId;
        obj.NetworkId = new NetworkId(id);
        obj.OwnerId = ownerId;
        obj.PrefabKey = prefabKey;
        _objects[id] = entity;

        var writer = new NetWriter(48);
        writer.WriteMessageType(MessageType.Spawn);
        WriteSpawnBody(writer, id, ownerId, prefabKey, entity);
        _net.ServerBroadcast(writer.Written);
        return entity;
    }

    /// <summary>Server-only: despawns a networked object everywhere.</summary>
    public void ServerDespawn(Entity entity)
    {
        if (!_net.IsServer)
        {
            Log.CoreWarn("Spot.Net: ServerDespawn is server-only.");
            return;
        }

        if (!entity.TryGetComponent(out NetworkObject? obj) || !obj.NetworkId.IsValid)
        {
            return;
        }

        uint id = obj.NetworkId.Value;
        _objects.Remove(id);

        var writer = new NetWriter(8);
        writer.WriteMessageType(MessageType.Despawn);
        writer.WriteUInt(id);
        _net.ServerBroadcast(writer.Written);

        _net.Scene?.Destroy(entity);
    }

    // A client connected: catch it up on every existing object, then spawn its player (if configured).
    public void OnClientConnected(Connection connection)
    {
        if (!_net.IsServer)
        {
            return;
        }

        foreach (KeyValuePair<uint, Entity> kv in _objects)
        {
            Entity entity = kv.Value;
            if (!entity.IsValid || !entity.TryGetComponent(out NetworkObject? obj))
            {
                continue;
            }

            var writer = new NetWriter(48);
            writer.WriteMessageType(MessageType.Spawn);
            WriteSpawnBody(writer, obj.NetworkId.Value, obj.OwnerId, obj.PrefabKey, entity);
            _net.ServerSend(connection.Id, writer.Written);
        }

        // Send the current value of every synchronized variable so a late-joiner starts in sync.
        foreach (KeyValuePair<uint, Entity> kv in _objects)
        {
            if (!kv.Value.IsValid)
            {
                continue;
            }

            List<NetworkBehaviour> behaviours = GetBehaviours(kv.Value);
            for (int b = 0; b < behaviours.Count; b++)
            {
                IReadOnlyList<ISyncVar> syncVars = behaviours[b].SyncVars;
                for (int s = 0; s < syncVars.Count; s++)
                {
                    SendSyncVar(kv.Key, (byte)b, (byte)s, syncVars[s], connection.Id);
                }
            }
        }

        if (_net.PlayerPrefab is not null)
        {
            ServerSpawn(_net.PlayerPrefab, connection.Id);
        }
    }

    // Broadcasts every synchronized variable whose value changed since the last tick, then clears its flag.
    private void SendDirtySyncVars()
    {
        foreach (KeyValuePair<uint, Entity> kv in _objects)
        {
            if (!kv.Value.IsValid)
            {
                continue;
            }

            List<NetworkBehaviour> behaviours = GetBehaviours(kv.Value);
            for (int b = 0; b < behaviours.Count; b++)
            {
                IReadOnlyList<ISyncVar> syncVars = behaviours[b].SyncVars;
                for (int s = 0; s < syncVars.Count; s++)
                {
                    if (syncVars[s].Dirty)
                    {
                        SendSyncVar(kv.Key, (byte)b, (byte)s, syncVars[s], toConnection: null);
                        syncVars[s].ClearDirty();
                    }
                }
            }
        }
    }

    // A client left: despawn everything it owned.
    public void OnClientDisconnected(Connection connection)
    {
        if (!_net.IsServer)
        {
            return;
        }

        List<Entity> owned = new();
        foreach (Entity entity in _objects.Values)
        {
            if (entity.IsValid && entity.TryGetComponent(out NetworkObject? obj) && obj.OwnerId == connection.Id)
            {
                owned.Add(entity);
            }
        }

        foreach (Entity entity in owned)
        {
            ServerDespawn(entity);
        }
    }

    // Fired at the network tick: the server broadcasts every object's transform; a pure client sends the
    // transforms of the objects it owns up to the server (which validates ownership and relays them).
    public void OnTick()
    {
        if (_net.Scene is null)
        {
            return;
        }

        if (_net.IsServer)
        {
            NetWriter? snapshot = BuildSnapshot(static (_, _) => true);
            if (snapshot is not null)
            {
                _net.ServerBroadcast(snapshot.Written);
            }

            SendDirtySyncVars();
        }
        else if (_net.Role == NetworkRole.Client)
        {
            int localId = _net.LocalConnectionId;
            NetWriter? snapshot = BuildSnapshot((_, obj) => obj.OwnerId == localId);
            if (snapshot is not null)
            {
                _net.SendToServerInternal(snapshot.Written);
            }
        }
    }

    // Routes an engine replication message to the right handler. The reader is positioned after the type byte.
    public void HandleMessage(int fromConnectionId, MessageType type, NetReader reader)
    {
        switch (type)
        {
            case MessageType.Spawn:
                HandleSpawn(ref reader);
                break;
            case MessageType.Despawn:
                HandleDespawn(ref reader);
                break;
            case MessageType.StateSnapshot:
                HandleSnapshot(fromConnectionId, ref reader);
                break;
            case MessageType.Rpc:
                HandleRpc(fromConnectionId, ref reader);
                break;
            case MessageType.SyncVar:
                HandleSyncVar(ref reader);
                break;
        }
    }

    /// <summary>Client→server (or host-local): routes a server RPC to its handler.</summary>
    public void SendServerRpc(NetworkObject obj, string method, object[] args)
    {
        if (_net.IsServer)
        {
            // A host runs its own server RPCs directly (it is the server).
            if (_objects.TryGetValue(obj.NetworkId.Value, out Entity entity))
            {
                ExecuteRpc(entity, _net.LocalConnectionId, method, args);
            }

            return;
        }

        if (_net.Role != NetworkRole.Client)
        {
            return;
        }

        var writer = new NetWriter(32);
        writer.WriteMessageType(MessageType.Rpc);
        writer.WriteNetworkId(obj.NetworkId);
        writer.WriteString(method);
        if (RpcSerializer.WriteArgs(writer, args))
        {
            _net.SendToServerInternal(writer.Written, DeliveryMethod.Reliable);
        }
    }

    /// <summary>Server→clients: broadcasts a client RPC, and runs it on the host's local client too.</summary>
    public void SendClientRpc(NetworkObject obj, string method, object[] args)
    {
        if (!_net.IsServer)
        {
            Log.CoreWarn("Spot.Net: InvokeClientRpc is server-only.");
            return;
        }

        var writer = new NetWriter(32);
        writer.WriteMessageType(MessageType.Rpc);
        writer.WriteNetworkId(obj.NetworkId);
        writer.WriteString(method);
        if (!RpcSerializer.WriteArgs(writer, args))
        {
            return;
        }

        _net.ServerBroadcast(writer.Written);

        if (_net.IsHost && _objects.TryGetValue(obj.NetworkId.Value, out Entity entity))
        {
            ExecuteRpc(entity, -1, method, args);
        }
    }

    private void HandleRpc(int fromConnectionId, ref NetReader reader)
    {
        NetworkId id = reader.ReadNetworkId();
        string method = reader.ReadString();
        if (!RpcSerializer.ReadArgs(ref reader, out object?[] args))
        {
            return;
        }

        if (_objects.TryGetValue(id.Value, out Entity entity) && entity.IsValid)
        {
            ExecuteRpc(entity, fromConnectionId, method, args);
        }
    }

    private void ExecuteRpc(Entity entity, int fromConnectionId, string method, object?[] args)
    {
        foreach (NetworkBehaviour behaviour in GetBehaviours(entity))
        {
            if (!RpcRegistry.TryGet(behaviour.GetType(), method, out RpcMethod rpc))
            {
                continue;
            }

            // Direction: a server RPC only runs on the server, a client RPC only on a client.
            if (rpc.Kind == RpcKind.Server && !_net.IsServer)
            {
                return;
            }

            if (rpc.Kind == RpcKind.Client && !_net.IsClient)
            {
                return;
            }

            // Authority: a server RPC arriving from a remote client is honored only for the object it owns.
            if (rpc.Kind == RpcKind.Server && fromConnectionId >= 0 &&
                (!entity.TryGetComponent(out NetworkObject? obj) || obj.OwnerId != fromConnectionId))
            {
                Log.CoreWarn("Spot.Net: rejected ServerRpc '{0}' from connection {1} (not the owner).", method, fromConnectionId);
                return;
            }

            try
            {
                rpc.Method.Invoke(behaviour, args);
            }
            catch (Exception ex)
            {
                Log.CoreError("Spot.Net: RPC '{0}' threw: {1}", method, ex);
            }

            return;
        }
    }

    private void HandleSyncVar(ref NetReader reader)
    {
        uint id = reader.ReadUInt();
        byte behaviourIndex = reader.ReadByte();
        byte syncVarIndex = reader.ReadByte();
        if (reader.Overflow || !_objects.TryGetValue(id, out Entity entity) || !entity.IsValid)
        {
            return;
        }

        List<NetworkBehaviour> behaviours = GetBehaviours(entity);
        if (behaviourIndex >= behaviours.Count)
        {
            return;
        }

        IReadOnlyList<ISyncVar> syncVars = behaviours[behaviourIndex].SyncVars;
        if (syncVarIndex < syncVars.Count)
        {
            syncVars[syncVarIndex].Read(ref reader);
        }
    }

    private void SendSyncVar(uint id, byte behaviourIndex, byte syncVarIndex, ISyncVar syncVar, int? toConnection)
    {
        var writer = new NetWriter(16);
        writer.WriteMessageType(MessageType.SyncVar);
        writer.WriteUInt(id);
        writer.WriteByte(behaviourIndex);
        writer.WriteByte(syncVarIndex);
        if (!syncVar.Write(writer))
        {
            return;
        }

        if (toConnection.HasValue)
        {
            _net.ServerSend(toConnection.Value, writer.Written);
        }
        else
        {
            _net.ServerBroadcast(writer.Written);
        }
    }

    private static List<NetworkBehaviour> GetBehaviours(Entity entity)
    {
        var list = new List<NetworkBehaviour>();
        if (entity.TryGetComponent(out ScriptComponent? scripts))
        {
            foreach (ScriptInstance item in scripts.Items)
            {
                if (item.Instance is NetworkBehaviour behaviour)
                {
                    list.Add(behaviour);
                }
            }
        }

        return list;
    }

    /// <summary>Smooths every non-owned replicated transform toward its latest received value.</summary>
    public void ApplyInterpolation(float deltaTime)
    {
        if (_net.Scene is null || _objects.Count == 0)
        {
            return;
        }

        // Exponential smoothing with a time constant of the interpolation delay: frame-rate independent and
        // stable, and a no-op where target == current (e.g. on the authoritative server).
        float tau = MathF.Max(NetworkSettings.InterpolationDelaySeconds, 0.0001f);
        float alpha = 1f - MathF.Exp(-deltaTime / tau);
        int localId = _net.LocalConnectionId;

        foreach (Entity entity in _objects.Values)
        {
            if (!entity.IsValid ||
                !entity.TryGetComponent(out NetworkTransform? nt) || !nt.HasTarget ||
                !entity.TryGetComponent(out NetworkObject? obj) || obj.OwnerId == localId ||
                !entity.TryGetComponent(out TransformComponent? transform))
            {
                continue;
            }

            if (nt.SyncPosition)
            {
                transform.Position = Vector3.Lerp(transform.Position, nt.TargetPosition, alpha);
            }

            if (nt.SyncRotation)
            {
                transform.Rotation = Vector3.Lerp(transform.Rotation, nt.TargetRotation, alpha);
            }
        }
    }

    /// <summary>Clears the registry and destroys the objects it tracked. Called when a session ends.</summary>
    public void Reset()
    {
        Scene? scene = _net.Scene;
        if (scene is not null)
        {
            foreach (Entity entity in _objects.Values)
            {
                if (entity.IsValid)
                {
                    scene.Destroy(entity);
                }
            }
        }

        _objects.Clear();
        _nextId = 0;
    }

    private void HandleSpawn(ref NetReader reader)
    {
        uint id = reader.ReadUInt();
        int owner = reader.ReadInt();
        string prefabKey = reader.ReadString();
        Vector3 position = reader.ReadVector3();
        Vector3 rotation = reader.ReadVector3();
        if (reader.Overflow || _objects.ContainsKey(id))
        {
            return;
        }

        Scene? scene = _net.Scene;
        if (scene is null)
        {
            return;
        }

        if (!NetworkPrefabs.TryGet(prefabKey, out Func<Scene, Entity> factory))
        {
            Log.CoreError("Spot.Net: received spawn for unknown prefab '{0}'.", prefabKey);
            return;
        }

        Entity entity = factory(scene);
        NetworkObject obj = entity.TryGetComponent(out NetworkObject? existing) ? existing : entity.AddComponent(new NetworkObject());
        obj.NetworkId = new NetworkId(id);
        obj.OwnerId = owner;
        obj.PrefabKey = prefabKey;

        if (entity.TryGetComponent(out TransformComponent? transform))
        {
            transform.Position = position;
            transform.Rotation = rotation;
        }

        if (entity.TryGetComponent(out NetworkTransform? nt))
        {
            nt.TargetPosition = position;
            nt.TargetRotation = rotation;
            nt.HasTarget = true;
        }

        _objects[id] = entity;
    }

    private void HandleDespawn(ref NetReader reader)
    {
        uint id = reader.ReadUInt();
        if (reader.Overflow || !_objects.Remove(id, out Entity entity))
        {
            return;
        }

        if (entity.IsValid)
        {
            _net.Scene?.Destroy(entity);
        }
    }

    private void HandleSnapshot(int fromConnectionId, ref NetReader reader)
    {
        ushort count = reader.ReadUInt16();
        int localId = _net.LocalConnectionId;

        for (int i = 0; i < count; i++)
        {
            uint id = reader.ReadUInt();
            Vector3 position = reader.ReadVector3();
            Vector3 rotation = reader.ReadVector3();
            if (reader.Overflow)
            {
                return;
            }

            if (!_objects.TryGetValue(id, out Entity entity) ||
                !entity.IsValid ||
                !entity.TryGetComponent(out NetworkObject? obj))
            {
                continue;
            }

            if (_net.IsServer)
            {
                // Authority: a client may only move the objects it owns. Snap the server's copy so the
                // relayed value is fresh, and record it as the target so a host smooths toward it too.
                if (obj.OwnerId != fromConnectionId)
                {
                    continue;
                }

                ApplyIncomingTransform(entity, position, rotation, snap: true);
            }
            else
            {
                // We drive our own object locally; only smooth the others.
                if (obj.OwnerId == localId)
                {
                    continue;
                }

                ApplyIncomingTransform(entity, position, rotation, snap: false);
            }
        }
    }

    private static void ApplyIncomingTransform(Entity entity, Vector3 position, Vector3 rotation, bool snap)
    {
        if (entity.TryGetComponent(out NetworkTransform? nt))
        {
            nt.TargetPosition = position;
            nt.TargetRotation = rotation;
            nt.HasTarget = true;
        }

        if (snap && entity.TryGetComponent(out TransformComponent? transform))
        {
            transform.Position = position;
            transform.Rotation = rotation;
        }
    }

    // Builds a StateSnapshot of every object for which include(entity, obj) is true, or null if none match.
    private NetWriter? BuildSnapshot(Func<Entity, NetworkObject, bool> include)
    {
        var entries = new List<(uint Id, Vector3 Pos, Vector3 Rot)>();
        foreach (KeyValuePair<uint, Entity> kv in _objects)
        {
            Entity entity = kv.Value;
            if (!entity.IsValid ||
                !entity.TryGetComponent(out NetworkObject? obj) ||
                !entity.HasComponent<NetworkTransform>() ||
                !include(entity, obj) ||
                !entity.TryGetComponent(out TransformComponent? transform))
            {
                continue;
            }

            entries.Add((kv.Key, transform.Position, transform.Rotation));
        }

        if (entries.Count == 0)
        {
            return null;
        }

        var writer = new NetWriter(4 + (entries.Count * 28));
        writer.WriteMessageType(MessageType.StateSnapshot);
        writer.WriteUInt16((ushort)Math.Min(entries.Count, ushort.MaxValue));
        foreach ((uint id, Vector3 pos, Vector3 rot) in entries)
        {
            writer.WriteUInt(id);
            writer.WriteVector3(pos);
            writer.WriteVector3(rot);
        }

        return writer;
    }

    private static void WriteSpawnBody(NetWriter writer, uint id, int ownerId, string prefabKey, Entity entity)
    {
        writer.WriteUInt(id);
        writer.WriteInt(ownerId);
        writer.WriteString(prefabKey);

        Vector3 position = Vector3.Zero;
        Vector3 rotation = Vector3.Zero;
        if (entity.TryGetComponent(out TransformComponent? transform))
        {
            position = transform.Position;
            rotation = transform.Rotation;
        }

        writer.WriteVector3(position);
        writer.WriteVector3(rotation);
    }
}
