using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using SysWebSocket = System.Net.WebSockets.WebSocket;

namespace Birko.Communication.WebSocket.Proxy
{
    /// <summary>
    /// Relays two connected WebSockets to each other until both have closed — frame by frame, so nothing is reassembled
    /// and memory is one buffer per direction.
    /// </summary>
    /// <remarks>
    /// Each frame keeps its message type and <c>EndOfMessage</c>. A close from one side is forwarded to the other with
    /// its status and description; a side that drops without a close leaves the other closed with
    /// <see cref="WebSocketCloseStatus.EndpointUnavailable"/>. When the closing handshake does not finish within the
    /// close timeout, both sockets are aborted.
    /// </remarks>
    internal static class WebSocketRelay
    {
        internal static async Task RunAsync(SysWebSocket a, SysWebSocket b, int bufferSize, TimeSpan closeTimeout, CancellationToken cancellationToken)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSize), bufferSize, "Must be positive.");

            using var pumps = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var aToB = PumpAsync(a, b, bufferSize, pumps.Token);
            var bToA = PumpAsync(b, a, bufferSize, pumps.Token);

            var first = await Task.WhenAny(aToB, bToA).ConfigureAwait(false);
            var second = first == aToB ? bToA : aToB;

            using (var delay = new CancellationTokenSource())
            {
                if (await Task.WhenAny(second, Task.Delay(closeTimeout, delay.Token)).ConfigureAwait(false) != second)
                {
                    pumps.Cancel();
                }
                delay.Cancel();
            }

            await second.ConfigureAwait(false);

            AbortUnlessClosed(a);
            AbortUnlessClosed(b);
        }

        private static async Task PumpAsync(SysWebSocket source, SysWebSocket destination, int bufferSize, CancellationToken cancellationToken)
        {
            var buffer = new byte[bufferSize];
            try
            {
                while (true)
                {
                    var result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        var status = result.CloseStatus ?? WebSocketCloseStatus.Empty;
                        await TryCloseOutputAsync(destination, status, status == WebSocketCloseStatus.Empty ? null : result.CloseStatusDescription)
                            .ConfigureAwait(false);
                        return;
                    }

                    await destination.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is ObjectDisposedException || ex is IOException)
            {
                // The source dropped without a close (or the destination failed, which its own pump reports the same way).
                await TryCloseOutputAsync(destination, WebSocketCloseStatus.EndpointUnavailable, "Peer connection lost").ConfigureAwait(false);
            }
        }

        private static async Task TryCloseOutputAsync(SysWebSocket socket, WebSocketCloseStatus status, string? description)
        {
            if (socket.State != WebSocketState.Open && socket.State != WebSocketState.CloseReceived)
            {
                return;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await socket.CloseOutputAsync(status, description, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is ObjectDisposedException || ex is ArgumentException)
            {
                // Already gone, or a status the sending API refuses to forward: the abort in RunAsync ends it.
            }
        }

        private static void AbortUnlessClosed(SysWebSocket socket)
        {
            if (socket.State != WebSocketState.Closed)
            {
                socket.Abort();
            }
        }
    }
}
