using Spot.Core;
using Spot.Scenes;

namespace Spot.Net;

/// <summary>
/// The entry point to Spot networking: it owns the active <see cref="ITransport"/>, tracks the session
/// role and connections, pumps inbound/outbound traffic, and routes messages. It is a plain singleton (not
/// an engine service, which the browser build does not have) driven each frame — on desktop and wasm alike
/// — from the networking scene systems, or manually in tests.
/// </summary>
/// <remarks>
/// Topology is server-authoritative: one native host acts as the server; desktop and browser builds join
/// as clients. A browser cannot listen, so <see cref="StartServer"/>/<see cref="StartHost"/> only work on a
/// desktop build. Nothing here throws out to the caller — bad input and lost connections are logged.
/// </remarks>
public sealed partial class NetworkManager
{
    private const int LocalClientId = 0;

    // Maps an active scene to the manager driving it, so networked scripts resolve their own manager even
    // when several run in one process (tests, split-screen tooling). The app's shared Instance is the fallback.
    private static readonly Dictionary<Scene, NetworkManager> s_byScene = new();

    private readonly Dictionary<int, Connection> _connections = new();
    private ITransport? _transport;
    private ISystem? _receiveSystem;
    private ISystem? _sendSystem;
    private float _tickAccumulator;

    // Internal so tests can spin up independent host/client managers in one process (the app uses the
    // shared Instance). Nothing forces a single instance; the singleton is just the convenient entry point.
    internal NetworkManager()
    {
        Replication = new NetworkReplication(this);
        ReplicationMessage += Replication.HandleMessage;
        ClientConnected += Replication.OnClientConnected;
        ClientDisconnected += Replication.OnClientDisconnected;
        NetworkTick += Replication.OnTick;
    }

    /// <summary>The scene networked objects live in. Defaults to the active scene when a session starts.</summary>
    public Scene? Scene { get; set; }

    /// <summary>
    /// When set on the server/host, the prefab spawned as each connecting client's own object (and
    /// despawned when they leave). Leave null to spawn players yourself from <see cref="ClientConnected"/>.
    /// </summary>
    public string? PlayerPrefab { get; set; }

    // The replication engine: the live-object registry, spawn/despawn, and transform snapshots.
    internal NetworkReplication Replication { get; }

    /// <summary>The active network manager, created on first access.</summary>
    public static NetworkManager Instance { get; } = new NetworkManager();

    /// <summary>The role the local peer is playing.</summary>
    public NetworkRole Role { get; private set; } = NetworkRole.None;

    /// <summary>Whether the local peer is authoritative (a dedicated server or a listen-server host).</summary>
    public bool IsServer => Role is NetworkRole.Server or NetworkRole.Host;

    /// <summary>Whether the local peer runs a client (a remote client or a listen-server host's local client).</summary>
    public bool IsClient => Role is NetworkRole.Client or NetworkRole.Host;

    /// <summary>Whether the local peer is a listen server (server and client in one process).</summary>
    public bool IsHost => Role == NetworkRole.Host;

    /// <summary>
    /// This peer's own client id. A host's local client is <c>0</c>; a remote client learns its id from the
    /// server's welcome; a dedicated server has none (<c>-1</c>).
    /// </summary>
    public int LocalConnectionId { get; private set; } = -1;

    /// <summary>The connected clients (server/host only).</summary>
    public IReadOnlyCollection<Connection> Connections => _connections.Values;

    /// <summary>Raised on the server when a client connects.</summary>
    public event Action<Connection>? ClientConnected;

    /// <summary>Raised on the server when a client disconnects.</summary>
    public event Action<Connection>? ClientDisconnected;

    /// <summary>Raised on a client once its connection to the server is established.</summary>
    public event Action? ConnectedToServer;

    /// <summary>Raised on a client when its connection to the server is closed or lost.</summary>
    public event Action? DisconnectedFromServer;

    /// <summary>
    /// Raised for an application message sent via <see cref="SendToServer"/>/<see cref="SendToClient"/>/
    /// <see cref="SendToAll"/>. The first argument is the sender's connection id (the server is <c>0</c> as
    /// seen by a client); the reader is positioned at the start of the payload.
    /// </summary>
    public event Action<int, NetReader>? MessageReceived;

    // Raised for engine replication messages (spawn, snapshot, rpc…). The replication layer subscribes; the
    // reader is positioned right after the message-type byte. Kept internal so games use the typed API.
    internal event Action<int, MessageType, NetReader>? ReplicationMessage;

    // Raised each network tick (at NetworkSettings.TickRate) while in a session. The replication layer
    // sends outbound state here: the server broadcasts snapshots; a client sends its owned transforms.
    internal event Action? NetworkTick;

    /// <summary>Starts a listen server: authoritative server plus a local client. Desktop only.</summary>
    /// <param name="port">The port to listen on; defaults to <see cref="NetworkSettings.Port"/>.</param>
    public void StartHost(int? port = null)
    {
        if (!TryStartServerTransport(port ?? NetworkSettings.Port))
        {
            return;
        }

        Role = NetworkRole.Host;
        LocalConnectionId = LocalClientId;
        _connections[LocalClientId] = new Connection(LocalClientId);
        AttachSession();

        // The host's own player, if one is configured (remote clients get theirs on connect).
        if (PlayerPrefab is not null)
        {
            Replication.ServerSpawn(PlayerPrefab, LocalClientId);
        }

        Log.CoreInfo("Spot.Net: started host.");
    }

    /// <summary>Starts a dedicated server. Desktop only.</summary>
    /// <param name="port">The port to listen on; defaults to <see cref="NetworkSettings.Port"/>.</param>
    public void StartServer(int? port = null)
    {
        if (!TryStartServerTransport(port ?? NetworkSettings.Port))
        {
            return;
        }

        Role = NetworkRole.Server;
        LocalConnectionId = -1;
        AttachSession();
        Log.CoreInfo("Spot.Net: started dedicated server.");
    }

    /// <summary>Connects to a server as a client.</summary>
    /// <param name="address">The server address; defaults to <see cref="NetworkSettings.DefaultAddress"/>.</param>
    /// <param name="port">The server port; defaults to <see cref="NetworkSettings.Port"/>.</param>
    public void StartClient(string? address = null, int? port = null)
    {
        if (Role != NetworkRole.None)
        {
            Log.CoreWarn("Spot.Net: already in a session; call Stop() first.");
            return;
        }

        var transport = new WebSocketClientTransport();
        _transport = transport;
        Role = NetworkRole.Client;
        LocalConnectionId = -1;
        AttachSession();
        transport.StartClient(address ?? NetworkSettings.DefaultAddress, port ?? NetworkSettings.Port);
    }

    /// <summary>Ends the session, closing the transport and clearing all connections.</summary>
    public void Stop()
    {
        DetachSession();
        _transport?.Stop();
        _transport = null;
        _connections.Clear();
        Replication.Reset();
        Role = NetworkRole.None;
        LocalConnectionId = -1;
        _tickAccumulator = 0f;
    }

    // Binds the session to a scene and installs the networking systems that pump it each frame: NetworkReceive
    // (poll + apply inbound) before the built-ins, NetworkSend (tick outbound) after scripts. Runs on desktop
    // and browser alike because scene systems tick on both. Falls back to the active scene when none is set.
    private void AttachSession()
    {
        Scene ??= SceneManager.Current;
        if (Scene is null)
        {
            Log.CoreWarn("Spot.Net: no active scene when starting a session; set NetworkManager.Scene so replication can run.");
            return;
        }

        s_byScene[Scene] = this;
        _receiveSystem = new DelegateSystem(NetworkSystemOrder.Receive, (_, dt) =>
        {
            Poll();
            Replication.ApplyInterpolation(dt);
        });
        _sendSystem = new DelegateSystem(NetworkSystemOrder.Send, (_, dt) => Tick(dt));
        Scene.RegisterSystem(_receiveSystem);
        Scene.RegisterSystem(_sendSystem);
    }

    // Resolves the manager driving a given scene, used by networked scripts to route RPCs to the right session.
    internal static bool TryGetForScene(Scene scene, out NetworkManager manager) => s_byScene.TryGetValue(scene, out manager!);

    private void DetachSession()
    {
        if (Scene is not null)
        {
            if (s_byScene.TryGetValue(Scene, out NetworkManager? owner) && ReferenceEquals(owner, this))
            {
                s_byScene.Remove(Scene);
            }

            if (_receiveSystem is not null)
            {
                Scene.Systems.Remove(_receiveSystem);
            }

            if (_sendSystem is not null)
            {
                Scene.Systems.Remove(_sendSystem);
            }
        }

        _receiveSystem = null;
        _sendSystem = null;
        Scene = null;
    }

    // Creates and starts the server transport for host/server. The concrete server type is desktop-only, so
    // it is installed through a partial method that simply isn't compiled on the browser target — where this
    // then reports failure. Returns whether the transport started.
    private bool TryStartServerTransport(int port)
    {
        if (Role != NetworkRole.None)
        {
            Log.CoreWarn("Spot.Net: already in a session; call Stop() first.");
            return false;
        }

        InstallServerTransport();
        if (_transport is null)
        {
            Log.CoreError("Spot.Net: hosting a server is not supported on this platform (browser builds can only be clients).");
            return false;
        }

        _transport.StartServer(port, NetworkSettings.MaxConnections);
        return true;
    }

    // Implemented in NetworkManager.Server.cs on desktop (sets _transport to a WebSocket server); absent on
    // the browser target, leaving _transport null so hosting is cleanly reported as unsupported.
    partial void InstallServerTransport();

    /// <summary>
    /// Runs the full networking pipeline in one call: poll inbound, apply client interpolation, then tick
    /// outbound. The app splits these across ordered scene systems (see <see cref="NetworkSystems"/>); this
    /// convenience is for headless drivers and tests.
    /// </summary>
    /// <param name="deltaTime">Elapsed seconds since the last update.</param>
    public void Update(float deltaTime)
    {
        Poll();
        Replication.ApplyInterpolation(deltaTime);
        Tick(deltaTime);
    }

    /// <summary>Drains inbound transport events and dispatches messages. Called early in the frame.</summary>
    public void Poll() => _transport?.Poll(HandleTransportEvent);

    /// <summary>Advances the network-tick accumulator, firing outbound state at the configured rate.</summary>
    /// <param name="deltaTime">Elapsed seconds since the last update.</param>
    public void Tick(float deltaTime)
    {
        if (Role == NetworkRole.None || NetworkTick is null)
        {
            return;
        }

        float interval = NetworkSettings.TickInterval;
        _tickAccumulator += deltaTime;

        // Clamp so a long stall (breakpoint, load hitch) can't make us fire a burst of catch-up ticks.
        if (_tickAccumulator > interval * 5f)
        {
            _tickAccumulator = interval;
        }

        while (_tickAccumulator >= interval)
        {
            _tickAccumulator -= interval;
            NetworkTick.Invoke();
        }
    }

    /// <summary>
    /// Server-only: spawns a registered prefab as a networked object owned by <paramref name="ownerId"/>
    /// (<c>-1</c> = the server), replicating it to every client. Returns the spawned entity, or <c>null</c>
    /// if not the server or the prefab/scene is missing.
    /// </summary>
    /// <param name="prefabKey">The key the prefab was registered under (see <see cref="NetworkPrefabs"/>).</param>
    /// <param name="ownerId">The owning connection id, or <c>-1</c> for the server.</param>
    public Entity? ServerSpawn(string prefabKey, int ownerId = -1) => Replication.ServerSpawn(prefabKey, ownerId);

    /// <summary>Server-only: despawns a networked object everywhere. A no-op if not the server.</summary>
    /// <param name="entity">The networked entity to despawn.</param>
    public void ServerDespawn(Entity entity) => Replication.ServerDespawn(entity);

    /// <summary>Sends an application message to the server (client role), or loops back locally (host).</summary>
    /// <param name="data">The payload bytes.</param>
    /// <param name="method">The delivery guarantee.</param>
    public void SendToServer(ReadOnlySpan<byte> data, DeliveryMethod method = DeliveryMethod.Reliable)
    {
        var writer = new NetWriter(data.Length + 1);
        writer.WriteMessageType(MessageType.Raw);
        writer.WriteBytes(data);

        if (Role == NetworkRole.Client)
        {
            _transport?.Send(0, writer.Written, method);
        }
        else if (IsHost)
        {
            // The host is the server: deliver to local server logic as if it came from the local client.
            DispatchMessage(LocalConnectionId, writer.ToArray());
        }
        else
        {
            Log.CoreWarn("Spot.Net: SendToServer called without a client session.");
        }
    }

    /// <summary>Sends an application message to a single client (server role).</summary>
    /// <param name="connectionId">The target client's connection id.</param>
    /// <param name="data">The payload bytes.</param>
    /// <param name="method">The delivery guarantee.</param>
    public void SendToClient(int connectionId, ReadOnlySpan<byte> data, DeliveryMethod method = DeliveryMethod.Reliable)
    {
        if (!IsServer)
        {
            Log.CoreWarn("Spot.Net: SendToClient is server-only.");
            return;
        }

        var writer = new NetWriter(data.Length + 1);
        writer.WriteMessageType(MessageType.Raw);
        writer.WriteBytes(data);

        if (connectionId == LocalClientId && IsHost)
        {
            DispatchMessage(LocalClientId, writer.ToArray());
            return;
        }

        _transport?.Send(connectionId, writer.Written, method);
    }

    /// <summary>Sends an application message to every client (server role).</summary>
    /// <param name="data">The payload bytes.</param>
    /// <param name="method">The delivery guarantee.</param>
    public void SendToAll(ReadOnlySpan<byte> data, DeliveryMethod method = DeliveryMethod.Reliable)
    {
        if (!IsServer)
        {
            Log.CoreWarn("Spot.Net: SendToAll is server-only.");
            return;
        }

        var writer = new NetWriter(data.Length + 1);
        writer.WriteMessageType(MessageType.Raw);
        writer.WriteBytes(data);
        _transport?.Broadcast(writer.Written, method);

        if (IsHost)
        {
            DispatchMessage(LocalClientId, writer.ToArray());
        }
    }

    // Sends an already-framed engine message (starting with its MessageType) to one client. Used by the
    // replication layer; a no-op unless we are the server.
    internal void ServerSend(int connectionId, ReadOnlySpan<byte> data, DeliveryMethod method = DeliveryMethod.Reliable)
    {
        if (IsServer)
        {
            _transport?.Send(connectionId, data, method);
        }
    }

    // Broadcasts an already-framed engine message to every client. Used by the replication layer.
    internal void ServerBroadcast(ReadOnlySpan<byte> data, DeliveryMethod method = DeliveryMethod.Reliable)
    {
        if (IsServer)
        {
            _transport?.Broadcast(data, method);
        }
    }

    // Sends an already-framed engine message from a client up to the server. Used by the replication layer
    // (e.g. an owning client's transform snapshot). A no-op unless we are a pure client.
    internal void SendToServerInternal(ReadOnlySpan<byte> data, DeliveryMethod method = DeliveryMethod.Unreliable)
    {
        if (Role == NetworkRole.Client)
        {
            _transport?.Send(0, data, method);
        }
    }

    private void HandleTransportEvent(TransportEvent e)
    {
        switch (e.Kind)
        {
            case TransportEventKind.Connected:
                OnConnected(e.ConnectionId);
                break;
            case TransportEventKind.Disconnected:
                OnDisconnected(e.ConnectionId);
                break;
            case TransportEventKind.Data:
                DispatchMessage(e.ConnectionId, e.Payload);
                break;
        }
    }

    private void OnConnected(int connectionId)
    {
        if (IsServer)
        {
            var connection = new Connection(connectionId);
            _connections[connectionId] = connection;

            // Tell the client which id the server knows it by, then let the replication layer sync state.
            var welcome = new NetWriter(8);
            welcome.WriteMessageType(MessageType.Welcome);
            welcome.WriteInt(connectionId);
            _transport?.Send(connectionId, welcome.Written, DeliveryMethod.Reliable);

            ClientConnected?.Invoke(connection);
        }
        else
        {
            ConnectedToServer?.Invoke();
        }
    }

    private void OnDisconnected(int connectionId)
    {
        if (IsServer)
        {
            if (_connections.Remove(connectionId, out Connection? connection))
            {
                ClientDisconnected?.Invoke(connection);
            }
        }
        else
        {
            LocalConnectionId = -1;
            DisconnectedFromServer?.Invoke();
        }
    }

    private void DispatchMessage(int fromConnectionId, ReadOnlyMemory<byte> payload)
    {
        var reader = new NetReader(payload);
        MessageType type = reader.ReadMessageType();
        if (reader.Overflow)
        {
            return;
        }

        switch (type)
        {
            case MessageType.Raw:
                MessageReceived?.Invoke(fromConnectionId, reader);
                break;
            case MessageType.Welcome:
                int assignedId = reader.ReadInt();
                if (!reader.Overflow && !IsServer)
                {
                    LocalConnectionId = assignedId;
                }

                break;
            default:
                ReplicationMessage?.Invoke(fromConnectionId, type, reader);
                break;
        }
    }
}
