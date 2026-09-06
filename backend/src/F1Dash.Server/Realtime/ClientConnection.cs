using System.Net.WebSockets;
using System.Threading.Channels;

namespace F1Dash.Server.Realtime;

/// <summary>
/// One browser connection and its outbound queue.
///
/// The queue is bounded and a client that fills it is dropped rather than
/// accommodated. One stalled browser tab must never be able to back up the
/// shared reader for everyone else; the dropped client reconnects and receives
/// a fresh snapshot (docs/07 §backpressure).
/// </summary>
public sealed class ClientConnection(WebSocket socket, int capacity)
{
    /// <summary>Roughly two minutes of deltas at the observed rate.</summary>
    public const int DefaultCapacity = 500;

    private readonly Channel<ReadOnlyMemory<byte>> _queue =
        Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(capacity)
        {
            // Wait, NOT DropWrite. This is subtle and it matters: with
            // DropWrite, TryWrite silently discards the incoming delta and
            // returns TRUE, so a slow client would keep its connection while
            // quietly losing patches — its state would drift and neither side
            // would know. With Wait, TryWrite returns FALSE when the queue is
            // full, which is what lets us drop the client and force a clean
            // resync. (The write never actually blocks; we only ever TryWrite.)
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Set when the queue overflowed; the writer loop then closes.</summary>
    public bool Overflowed { get; private set; }

    /// <summary>
    /// Offers a frame. Returns false when the client is too slow, at which point
    /// the caller drops it.
    /// </summary>
    public bool Offer(ReadOnlyMemory<byte> payload)
    {
        if (_queue.Writer.TryWrite(payload)) return true;

        Overflowed = true;
        _queue.Writer.TryComplete();
        return false;
    }

    public void Complete() => _queue.Writer.TryComplete();

    /// <summary>Drains the queue to the socket until it completes or faults.</summary>
    public async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await socket
                    .SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, ct)
                    .ConfigureAwait(false);
            }

            if (Overflowed && socket.State == WebSocketState.Open)
            {
                // 1013 "try again later" — the client backs off and reconnects.
                await socket.CloseAsync(
                    (WebSocketCloseStatus)1013, "too slow", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (WebSocketException)
        {
            // The client vanished. Nothing to do; the broadcaster drops us.
        }
    }
}
