# Networking

Spot ships a small, server-authoritative networking foundation in a separate library, **`Spot.Net`**
(namespace `Spot.Net`). It gives you the plumbing to build a multiplayer game — connect peers, spawn
networked objects, replicate their transforms, and send RPCs and synchronized variables — while leaving
the game-specific decisions to you. It is deliberately basic: there is no prediction, rollback, or lag
compensation. Think of it as the base you build a game on, not a finished netcode stack.

`Spot.Net` is a separate project so the runtime stays lean; reference it from your game (the sandbox and
editor already do).

## Quick start (5 steps)

1. **Reference** `Spot.Net` from your game project.
2. **Register a prefab** that has a `NetworkObject` component so the server can spawn it by name:
   ```csharp
   NetworkPrefabs.Register("Player", "Assets/Prefabs/Player.sptprefab");
   ```
3. **Set the player prefab** on the manager so it auto-spawns one per connecting client:
   ```csharp
   NetworkManager.Instance.PlayerPrefab = "Player";
   ```
4. **Start a host** (one process acts as server + local player) or a **client** (connect to an existing
   server). From code:
   ```csharp
   NetworkManager.Instance.StartHost();   // server + local client on port 7777
   NetworkManager.Instance.StartClient("192.168.1.10");  // pure client
   ```
   Or via the developer console: `net_host` / `net_connect <address>`.
5. **Add `NetworkTransform`** to any entity you want to replicate position/rotation automatically, and
   **`NetworkBehaviour`** subclasses for custom RPCs and synchronized variables.

> **Remote clients:** `NetworkSettings.BindAddress` defaults to `"localhost"` (same machine only). Set it
> to `"+"` before calling `StartHost` to accept connections from other machines.

## Topology

Networking is **server-authoritative** with one of the peers acting as the server:

- A **host** is a listen server: the authoritative server *and* a local player in one process.
- A **dedicated server** is authoritative with no local player.
- A **client** connects to a server.

The server owns identity: it assigns each networked object its id and decides who spawns and despawns.
"Ownership" of an object only decides which peer reads local input and drives that object.

The default transport is **WebSocket**, which runs everywhere Spot does — desktop and browser/WASM — from
the same code. Two consequences follow from what a browser is allowed to do on the network:

- A **browser build can only be a client.** It cannot listen on a port, so the server/host is always a
  native (desktop) build. Desktop and browser clients connect to it.
- WebSocket is reliable and ordered (it runs over TCP). There is no unreliable channel today; the
  `DeliveryMethod.Unreliable` hint exists for a future UDP/WebRTC transport but currently behaves as
  reliable.

The transport sits behind an `ITransport` seam, so a UDP (desktop) or WebRTC (browser) transport can be
added later without touching anything above it.

## Where it runs in the frame

Networking is driven by two scene systems (so it ticks on desktop and browser alike, no engine service
required):

- **Receive** runs before every built-in system: it polls the transport and applies inbound state, so
  scripts and physics see the up-to-date world this frame.
- **Send** runs after your scripts: it ticks outbound state at a fixed rate, so what you send reflects the
  frame's resolved result.

The send rate is a fixed tick (default 30 Hz) independent of the frame rate; reliable events (spawns,
despawns, RPCs) are sent immediately and don't wait for the tick. All of this is set up for you when a
session starts and torn down when it stops.

## Starting a session

Everything goes through `NetworkManager.Instance`:

```csharp
NetworkManager.Instance.StartHost();                 // listen server + local player (desktop)
NetworkManager.Instance.StartServer();               // dedicated server (desktop)
NetworkManager.Instance.StartClient("127.0.0.1");    // client (desktop or browser)
NetworkManager.Instance.Stop();
```

Connection lifecycle is exposed as events: `ClientConnected`/`ClientDisconnected` on the server,
`ConnectedToServer`/`DisconnectedFromServer` on a client.

Defaults (port, address, tick rate, interpolation delay, max connections) live on the static
`NetworkSettings`. Set `NetworkSettings.UseSsl = true` before calling `StartClient` when your game is
served over HTTPS (e.g. a deployed WASM build) — this switches the transport from `ws://` to `wss://`,
which browsers require to avoid blocking the connection as mixed content. On desktop you can also drive a
session from the developer console after calling
`NetworkConsole.Install()`: `net_host [port]`, `net_connect [address] [port]`, `net_stop`, `net_status`.

## Networked objects and spawning

A networked object is an entity carrying a `NetworkObject` component (its `NetworkId`, owner, and prefab
key). You never create these on a client directly — the **server spawns** them and every client recreates
them from a shared **prefab registry**:

```csharp
// Register the same prefab on every peer at startup (server and clients).
NetworkSpawner.RegisterPrefab("Player", scene =>
{
    Entity e = scene.Instantiate("Player");
    e.AddComponent(new NetworkObject());
    e.AddComponent(new NetworkTransform());
    e.AddScript<PlayerController>();
    return e;
});

// On the server only:
Entity player = NetworkSpawner.ServerSpawn("Player", ownerId: connection.Id)!.Value;
NetworkSpawner.ServerDespawn(player);
```

Because a spawn travels as a prefab *key* (not a scene reference), each peer builds the object from its own
factory. Set `NetworkManager.Instance.PlayerPrefab` and the server will automatically spawn that prefab for
each connecting client (and despawn it when they leave); a host gets one too.

The scene networked objects live in is `NetworkManager.Instance.Scene`, which defaults to the active scene
when the session starts.

## Replicating transforms

Add a `NetworkTransform` alongside `NetworkObject` and the object's position/rotation replicate
automatically. The owner samples and sends its transform each tick; every other peer smooths toward the
latest received value (simple interpolation, tuned by `NetworkSettings.InterpolationDelaySeconds`) so
movement looks continuous. The server validates that a client may only move the objects it owns.

## Behaviours, RPCs, and synchronized variables

Derive gameplay scripts from `NetworkBehaviour` (instead of `EntityBehaviour`) to get `IsServer`,
`IsClient`, `IsOwner`, RPCs, and synchronized variables:

```csharp
public sealed class PlayerController : NetworkBehaviour
{
    private readonly SyncVar<int> _health = default!; // set in the constructor via Sync<T>()

    public PlayerController() => _health = Sync<int>(100);

    public override void OnUpdate(float dt)
    {
        if (!IsOwner) return;                                   // only the owner reads input
        var t = GetComponent<TransformComponent>();
        var move = new Vector3(Input.GetAxis("horizontal"), 0, Input.GetAxis("vertical"));
        t.Position += move * 5f * dt;                           // NetworkTransform replicates this

        if (Input.GetActionDown("fire"))
            InvokeServerRpc(nameof(Fire), t.Position);          // client → server
    }

    [ServerRpc] private void Fire(Vector3 origin)               // runs on the server
    {
        if (IsServer) InvokeClientRpc(nameof(PlayHit), origin); // server → all clients
    }

    [ClientRpc] private void PlayHit(Vector3 at) { /* spawn an effect locally */ }
}
```

- **RPCs.** Mark a handler `[ServerRpc]` (runs on the server, invoked by the owning client) or `[ClientRpc]`
  (runs on clients, invoked by the server), and call it by name with `InvokeServerRpc`/`InvokeClientRpc`.
  The server rejects a `[ServerRpc]` from a client that doesn't own the target object. Arguments may be any
  of the network-serializable value types: `int`, `uint`, `float`, `bool`, `string`, `Vector3`,
  `Quaternion`, `NetworkId`, `byte`, `ushort`. RPC method names should be unique per networked entity.
- **SyncVars.** Declare a `SyncVar<T>` with `Sync<T>(initial)` (in the constructor, so its index matches on
  every peer). Set `.Value` on the server and every client — including late joiners — receives it; the
  element type is one of the serializable value types above.

RPC/SyncVar dispatch uses reflection over your behaviour's methods and fields. This is fine on desktop and
on a normal browser build; an aggressively trimmed/AOT wasm *publish* should preserve those members. (A
source generator is the intended future hardening for that case.)

## Connection health and limits

Each peer sends a periodic heartbeat, so a connection that is alive but quiet (a player standing still)
is not confused with one that died. A connection that goes silent past a timeout is dropped and surfaces
as a normal disconnect (`ClientDisconnected` / `DisconnectedFromServer`), so half-open connections don't
linger as "ghost" players. The transport also caps the size of a single inbound message and drops a peer
that exceeds it, bounding what a hostile or buggy client can make the server buffer. Tune all of these on
`NetworkSettings` (`HeartbeatIntervalSeconds`, `TimeoutSeconds` — 0 disables timeouts — and
`MaxMessageBytes`).

This is basic hardening, not a security boundary: there is no encryption, authentication, or anti-cheat.
Treat every client as untrusted and keep authority on the server.

## What it does not do (yet)

Out of scope for this foundation, left for you or a later version to build on top:

- Client-side prediction, reconciliation, rollback, lag compensation.
- Interest management / area-of-interest, snapshot delta compression or quantization.
- Matchmaking, lobbies, relays, dedicated hosting, NAT punchthrough.
- An unreliable channel, a UDP (desktop) or WebRTC (browser) transport.
- Encryption and anti-cheat.

## Testing locally

Run two instances of your game: `net_host` in one, `net_connect 127.0.0.1` in the other, and you'll see
each peer's player replicated. For same-machine LAN or a browser client, the server binds `localhost` by
default (no OS permission needed); to accept clients from other machines set `NetworkSettings.BindAddress`
to a routable address, which on Windows may require a URL reservation.

### Smoke-testing a browser client

A browser build is a networking client, so verify it against a native host:

1. **Start a host** in a desktop build of your game (or the editor's play mode) and `net_host`. The server
   must bind an address the browser can reach — `localhost` for a browser tab on the same machine (the
   default), or a routable address for another device.
2. **Build the browser client**: `dotnet run --project tools/Spot.Cli -- build browser --project <your>.sptproj`.
   This publishes a static site under `Build/browser` and bundles `Spot.Net` into the WASM app.
3. **Serve the site** over HTTP (a browser won't load WASM from `file://`), for example
   `dotnet serve -d Build/browser/wwwroot` or any static file server, and open it.
4. **Connect** from the page by calling `NetworkManager.Instance.StartClient("<host address>")` from your
   game code (the browser has no developer console). You should see the browser client join and the
   players replicate both ways.

> **Deployed over HTTPS?** Set `NetworkSettings.UseSsl = true` before `StartClient`. Browsers block
> `ws://` connections from HTTPS pages (mixed-content policy); `wss://` requires your server to be behind
> TLS (a reverse proxy such as nginx or Caddy works well for this).

Because the browser client uses `ClientWebSocket`, the same networking code paths run as on desktop; only
the underlying socket differs (the browser's native WebSocket).
