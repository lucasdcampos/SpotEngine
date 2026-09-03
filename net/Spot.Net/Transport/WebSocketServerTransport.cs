using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using Spot.Core;

namespace Spot.Net;

/// <summary>
/// The server half of the WebSocket transport, built on <see cref="HttpListener"/>. Desktop-only: a
/// browser build cannot listen on a port, so a networked game is always hosted by a native process while
/// clients (desktop and browser) connect in. By default it binds <c>localhost</c>, which needs no OS
/// permission and accepts same-machine clients; binding a routable address for LAN/remote play may require
/// a URL reservation on Windows (see the networking docs).
/// </summary>
public sealed class WebSocketServerTransport : ITransport
{
    private readonly ConcurrentQueue<TransportEvent> _events = new();
    private readonly ConcurrentDictionary<int, WebSocketPeer> _peers = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _nextConnectionId;
    private int _maxConnections;
    private volatile bool _running;

    /// <inheritdoc />
    public bool IsRunning => _running;

    /// <inheritdoc />
    public void StartServer(int port, int maxConnections)
    {
        if (_running)
        {
            Log.CoreWarn("Spot.Net: server transport already running.");
            return;
        }

        _maxConnections = Math.Max(1, maxConnections);
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://{NetworkSettings.BindAddress}:{port}/");

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            Log.CoreError("Spot.Net: failed to start server on {0}:{1} — {2}", NetworkSettings.BindAddress, port, ex.Message);
            return;
        }

        _listener = listener;
        _cts = new CancellationTokenSource();
        _running = true;
        Log.CoreInfo("Spot.Net: server listening on {0}:{1}", NetworkSettings.BindAddress, port);
        _ = AcceptLoopAsync(listener, _cts.Token);
    }

    /// <inheritdoc />
    public void StartClient(string address, int port) =>
        Log.CoreError("Spot.Net: WebSocketServerTransport cannot be a client; use StartServer.");

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Log.CoreTrace("Spot.Net: accept loop ended: {0}", ex.Message);
                }

                return;
            }

            _ = AcceptClientAsync(context);
        }
    }

    private async Task AcceptClientAsync(HttpListenerContext context)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        if (_peers.Count >= _maxConnections)
        {
            Log.CoreWarn("Spot.Net: rejecting client, server full ({0}).", _maxConnections);
            context.Response.StatusCode = 503;
            context.Response.Close();
            return;
        }

        WebSocketContext wsContext;
        try
        {
            wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Spot.Net: WebSocket handshake failed: {0}", ex.Message);
            return;
        }

        int id = Interlocked.Increment(ref _nextConnectionId);
        var peer = new WebSocketPeer(id, wsContext.WebSocket, _events);
        _peers[id] = peer;
        peer.Start();
        _events.Enqueue(new TransportEvent(TransportEventKind.Connected, id, ReadOnlyMemory<byte>.Empty));
    }

    /// <inheritdoc />
    public void Poll(Action<TransportEvent> onEvent)
    {
        while (_events.TryDequeue(out TransportEvent e))
        {
            if (e.Kind == TransportEventKind.Disconnected)
            {
                _peers.TryRemove(e.ConnectionId, out _);
            }

            onEvent(e);
        }
    }

    /// <inheritdoc />
    public void Send(int connectionId, ReadOnlySpan<byte> data, DeliveryMethod method)
    {
        if (_peers.TryGetValue(connectionId, out WebSocketPeer? peer))
        {
            peer.Send(data.ToArray());
        }
    }

    /// <inheritdoc />
    public void Broadcast(ReadOnlySpan<byte> data, DeliveryMethod method)
    {
        if (_peers.IsEmpty)
        {
            return;
        }

        // One shared copy handed to every peer's send queue.
        byte[] copy = data.ToArray();
        foreach (WebSocketPeer peer in _peers.Values)
        {
            peer.Send(copy);
        }
    }

    /// <inheritdoc />
    public void Disconnect(int connectionId)
    {
        if (_peers.TryGetValue(connectionId, out WebSocketPeer? peer))
        {
            peer.Close();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        _running = false;
        try
        {
            _cts?.Cancel();
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Spot.Net: error stopping server: {0}", ex.Message);
        }

        foreach (WebSocketPeer peer in _peers.Values)
        {
            peer.Close();
        }

        _peers.Clear();

        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch (Exception ex)
        {
            Log.CoreTrace("Spot.Net: error closing listener: {0}", ex.Message);
        }

        _listener = null;
        _cts = null;
    }
}
