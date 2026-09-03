using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using Spot.Core;

namespace Spot.Net;

/// <summary>
/// Wraps a single connected <see cref="WebSocket"/> — used for both the client's link to the server and
/// each of the server's client links — and pumps it with async receive/send loops. Inbound messages are
/// queued as <see cref="TransportEvent"/>s onto the shared transport queue; outbound messages go through a
/// channel so sends are serialized (a <see cref="WebSocket"/> forbids concurrent sends) without blocking
/// the game thread. All I/O is guarded: any failure closes the peer and reports a disconnect rather than
/// throwing, on desktop threads and the browser event loop alike.
/// </summary>
internal sealed class WebSocketPeer
{
    private const int ReceiveChunkSize = 8 * 1024;

    private readonly WebSocket _socket;
    private readonly ConcurrentQueue<TransportEvent> _events;
    private readonly Channel<byte[]> _outbound;
    private readonly CancellationTokenSource _cts = new();
    private int _closed;

    public WebSocketPeer(int id, WebSocket socket, ConcurrentQueue<TransportEvent> events)
    {
        Id = id;
        _socket = socket;
        _events = events;
        _outbound = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });
    }

    /// <summary>The transport-level connection id of this peer.</summary>
    public int Id { get; }

    /// <summary>Starts the receive and send loops. Call once after construction.</summary>
    public void Start()
    {
        // Fire-and-forget: the loops own their lifetime and surface completion as a Disconnected event.
        // Not awaited on purpose — on wasm these resume on the browser event loop between frames.
        _ = ReceiveLoopAsync();
        _ = SendLoopAsync();
    }

    /// <summary>Queues a message for sending. A no-op once the peer is closing.</summary>
    public void Send(byte[] data)
    {
        if (_closed != 0)
        {
            return;
        }

        _outbound.Writer.TryWrite(data);
    }

    /// <summary>Closes the peer, cancelling its loops and the socket. Reports a single Disconnected event.</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        _outbound.Writer.TryComplete();
        try
        {
            _cts.Cancel();
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Spot.Net: error cancelling peer {0}: {1}", Id, ex.Message);
        }

        // Best-effort courteous close; ignore failures (the socket may already be faulted).
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                _ = _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Log.CoreTrace("Spot.Net: error closing socket for peer {0}: {1}", Id, ex.Message);
        }

        _events.Enqueue(new TransportEvent(TransportEventKind.Disconnected, Id, ReadOnlyMemory<byte>.Empty));
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[ReceiveChunkSize];
        using var message = new MemoryStream();

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Close();
                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                // A copy the size of the assembled message; owned by the event so the buffer can be reused.
                byte[] payload = message.ToArray();
                _events.Enqueue(new TransportEvent(TransportEventKind.Data, Id, payload));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Close().
        }
        catch (Exception ex)
        {
            Log.CoreTrace("Spot.Net: receive loop ended for peer {0}: {1}", Id, ex.Message);
        }
        finally
        {
            Close();
        }
    }

    private async Task SendLoopAsync()
    {
        try
        {
            while (await _outbound.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (_outbound.Reader.TryRead(out byte[]? data))
                {
                    await _socket.SendAsync(
                        new ArraySegment<byte>(data),
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        _cts.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Close().
        }
        catch (Exception ex)
        {
            Log.CoreTrace("Spot.Net: send loop ended for peer {0}: {1}", Id, ex.Message);
            Close();
        }
    }
}
