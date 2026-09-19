using System.Collections.Concurrent;
using System.Net.WebSockets;
using Spot.Core;

namespace Spot.Net;

/// <summary>
/// The client half of the WebSocket transport, built on <see cref="ClientWebSocket"/>. This type is
/// platform-neutral: the same code connects from a desktop build and from a browser/wasm build, where the
/// .NET runtime maps <see cref="ClientWebSocket"/> onto the browser's native WebSocket. A client has a
/// single connection to the server, exposed with id <c>0</c>.
/// </summary>
public sealed class WebSocketClientTransport : ITransport
{
    private readonly ConcurrentQueue<TransportEvent> _events = new();
    private ClientWebSocket? _socket;
    private WebSocketPeer? _peer;
    private volatile bool _running;

    /// <inheritdoc />
    public bool IsRunning => _running;

    /// <inheritdoc />
    public void StartServer(int port, int maxConnections) =>
        Log.CoreError("Spot.Net: WebSocketClientTransport cannot host a server; use StartClient.");

    /// <inheritdoc />
    public void StartClient(string address, int port)
    {
        if (_running)
        {
            Log.CoreWarn("Spot.Net: client transport already running.");
            return;
        }

        _running = true;
        _ = ConnectAsync(address, port);
    }

    private async Task ConnectAsync(string address, int port)
    {
        var socket = new ClientWebSocket();
        _socket = socket;
        try
        {
            var uri = new Uri($"ws://{address}:{port}/");
            await socket.ConnectAsync(uri, CancellationToken.None).ConfigureAwait(false);

            var peer = new WebSocketPeer(0, socket, _events);
            _peer = peer;
            peer.Start();
            _events.Enqueue(new TransportEvent(TransportEventKind.Connected, 0, ReadOnlyMemory<byte>.Empty));
        }
        catch (Exception ex)
        {
            Log.CoreError("Spot.Net: failed to connect to {0}:{1} — {2}", address, port, ex.Message);
            _running = false;
            // Surface the failure as a disconnect so the manager can react (e.g. show a menu again).
            _events.Enqueue(new TransportEvent(TransportEventKind.Disconnected, 0, ReadOnlyMemory<byte>.Empty));
        }
    }

    /// <inheritdoc />
    public void Poll(Action<TransportEvent> onEvent)
    {
        while (_events.TryDequeue(out TransportEvent e))
        {
            if (e.Kind == TransportEventKind.Disconnected)
            {
                _running = false;
                _peer = null;
            }

            onEvent(e);
        }
    }

    /// <inheritdoc />
    public void Send(int connectionId, ReadOnlySpan<byte> data, DeliveryMethod method)
    {
        WebSocketPeer? peer = _peer;
        if (peer is null)
        {
            return;
        }

        peer.Send(data.ToArray());
    }

    /// <inheritdoc />
    public void Broadcast(ReadOnlySpan<byte> data, DeliveryMethod method) => Send(0, data, method);

    /// <inheritdoc />
    public void Disconnect(int connectionId) => _peer?.Close();

    /// <inheritdoc />
    public void Stop()
    {
        _running = false;
        _peer?.Close();
        _peer = null;
        _socket = null;
    }
}
