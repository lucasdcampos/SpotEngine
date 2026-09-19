using Spot.Core;
using Spot.Scenes;

namespace Spot.Net;

/// <summary>
/// The base class for networked gameplay scripts — the networking counterpart to
/// <see cref="EntityBehaviour"/>. It exposes the peer's role and this object's ownership, and adds RPCs
/// (<see cref="InvokeServerRpc"/>/<see cref="InvokeClientRpc"/> with <c>[ServerRpc]</c>/<c>[ClientRpc]</c>
/// handlers) and synchronized variables (<see cref="Sync{T}"/>). Attach it to an entity that also carries a
/// <see cref="NetworkObject"/> (networked prefabs do this).
/// </summary>
public abstract class NetworkBehaviour : EntityBehaviour
{
    private List<ISyncVar>? _syncVars;
    private Action? _onConnectedHandler;
    private Action? _onDisconnectedHandler;

    /// <summary>Whether the local peer is the authoritative server (or host).</summary>
    protected bool IsServer => Net.IsServer;

    /// <summary>Whether the local peer runs a client.</summary>
    protected bool IsClient => Net.IsClient;

    /// <summary>Whether the local peer owns this object — true where input should be read and the object driven.</summary>
    protected bool IsOwner => NetObject is { } obj && obj.OwnerId == Net.LocalConnectionId;

    /// <summary>This entity's <see cref="NetworkObject"/>, or <see langword="null"/> if it has none.</summary>
    protected NetworkObject? NetObject => Entity.TryGetComponent(out NetworkObject? obj) ? obj : null;

    // The manager that owns this script's scene (so two managers in one process — e.g. tests — stay
    // independent), falling back to the shared instance.
    private NetworkManager Net =>
        NetworkManager.TryGetForScene(Entity.Scene, out NetworkManager? manager) ? manager : NetworkManager.Instance;

    /// <summary>
    /// Declares a server-authoritative synchronized variable with an initial value. Set it on the server and
    /// clients receive the change automatically. Call this from the script's constructor or
    /// <see cref="EntityBehaviour.OnCreate"/> so its index is stable across peers.
    /// </summary>
    /// <typeparam name="T">A network-serializable value type.</typeparam>
    /// <param name="value">The initial value.</param>
    protected SyncVar<T> Sync<T>(T value = default!)
    {
        _syncVars ??= new List<ISyncVar>();
        var syncVar = new SyncVar<T>(value);
        _syncVars.Add(syncVar);
        return syncVar;
    }

    /// <summary>
    /// Invokes a <c>[ServerRpc]</c> method by name: called by the object's owning client, it runs on the
    /// server. A no-op if this object has no network identity yet.
    /// </summary>
    /// <param name="method">The handler method name (use <c>nameof</c>).</param>
    /// <param name="args">The arguments (network-serializable value types).</param>
    protected void InvokeServerRpc(string method, params object[] args)
    {
        if (NetObject is not { } obj || !obj.NetworkId.IsValid)
        {
            Log.CoreWarn("Spot.Net: InvokeServerRpc on an object without a network identity.");
            return;
        }

        Net.Replication.SendServerRpc(obj, method, args);
    }

    /// <summary>
    /// Invokes a <c>[ClientRpc]</c> method by name: called by the server, it runs on every client. A no-op if
    /// this object has no network identity yet or the local peer is not the server.
    /// </summary>
    /// <param name="method">The handler method name (use <c>nameof</c>).</param>
    /// <param name="args">The arguments (network-serializable value types).</param>
    protected void InvokeClientRpc(string method, params object[] args)
    {
        if (NetObject is not { } obj || !obj.NetworkId.IsValid)
        {
            Log.CoreWarn("Spot.Net: InvokeClientRpc on an object without a network identity.");
            return;
        }

        Net.Replication.SendClientRpc(obj, method, args);
    }

    /// <summary>
    /// Called on a client when its connection to the server is established. Override to initialize
    /// client-side state that depends on the session being live.
    /// </summary>
    protected virtual void OnNetworkConnected() { }

    /// <summary>
    /// Called on a client when its connection to the server is closed or lost. Override to clean up
    /// state that was set up in <see cref="OnNetworkConnected"/>.
    /// </summary>
    protected virtual void OnNetworkDisconnected() { }

    public override void OnCreate()
    {
        base.OnCreate();
        _onConnectedHandler = OnNetworkConnected;
        _onDisconnectedHandler = OnNetworkDisconnected;
        Net.ConnectedToServer += _onConnectedHandler;
        Net.DisconnectedFromServer += _onDisconnectedHandler;
    }

    public override void OnDestroy()
    {
        Net.ConnectedToServer -= _onConnectedHandler;
        Net.DisconnectedFromServer -= _onDisconnectedHandler;
        base.OnDestroy();
    }

    // The synchronized variables declared on this behaviour, in declaration order (their wire index).
    internal IReadOnlyList<ISyncVar> SyncVars => _syncVars ?? (IReadOnlyList<ISyncVar>)Array.Empty<ISyncVar>();
}
