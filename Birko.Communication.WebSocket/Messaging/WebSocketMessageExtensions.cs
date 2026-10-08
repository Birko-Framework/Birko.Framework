using System;
using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SysWebSocket = System.Net.WebSockets.WebSocket;

namespace Birko.Communication.WebSocket.Messaging
{
    /// <summary>How a <see cref="WebSocketMessageExtensions.ReceiveMessageAsync"/> call ended.</summary>
    public enum WebSocketReceiveOutcome
    {
        /// <summary>A whole message arrived; <see cref="WebSocketReceivedMessage.Data"/> holds it.</summary>
        Message,

        /// <summary>The peer closed the connection; the close was acknowledged.</summary>
        Closed,

        /// <summary>The message exceeded the cap; the socket was closed with <see cref="WebSocketCloseStatus.MessageTooBig"/>.</summary>
        TooBig
    }

    /// <summary>The result of receiving one whole WebSocket message.</summary>
    public sealed class WebSocketReceivedMessage
    {
        private WebSocketReceivedMessage(WebSocketReceiveOutcome outcome, WebSocketMessageType messageType, byte[] data,
            WebSocketCloseStatus? closeStatus, string? closeDescription)
        {
            Outcome = outcome;
            MessageType = messageType;
            Data = data;
            CloseStatus = closeStatus;
            CloseDescription = closeDescription;
        }

        public WebSocketReceiveOutcome Outcome { get; }

        /// <summary>Text or Binary for a <see cref="WebSocketReceiveOutcome.Message"/>; Close otherwise.</summary>
        public WebSocketMessageType MessageType { get; }

        /// <summary>The whole message; empty unless <see cref="Outcome"/> is <see cref="WebSocketReceiveOutcome.Message"/>.</summary>
        public byte[] Data { get; }

        /// <summary>The peer's close status for <see cref="WebSocketReceiveOutcome.Closed"/>; MessageTooBig for TooBig.</summary>
        public WebSocketCloseStatus? CloseStatus { get; }

        public string? CloseDescription { get; }

        public bool IsMessage => Outcome == WebSocketReceiveOutcome.Message;

        /// <summary>The message decoded as UTF-8.</summary>
        public string GetText() => Encoding.UTF8.GetString(Data);

        internal static WebSocketReceivedMessage ForMessage(WebSocketMessageType type, byte[] data) =>
            new(WebSocketReceiveOutcome.Message, type, data, null, null);

        internal static WebSocketReceivedMessage ForClose(WebSocketReceiveOutcome outcome, WebSocketCloseStatus? status, string? description) =>
            new(outcome, WebSocketMessageType.Close, Array.Empty<byte>(), status, description);
    }

    /// <summary>
    /// Whole-message receive for a raw <see cref="SysWebSocket"/> — the one reassembly loop the framework has.
    /// </summary>
    /// <remarks>
    /// TASK-537: a message may span several frames, and a loop that reads one <c>ReceiveAsync</c> parses a fragment; a
    /// loop that reassembles without a limit lets any client grow server memory without bound by never sending
    /// <c>EndOfMessage</c>. This does both halves. Like <c>ReceiveAsync</c>, it must not be called concurrently on one socket.
    /// </remarks>
    public static class WebSocketMessageExtensions
    {
        /// <summary>The cap <see cref="Servers.WebSocketServer"/> uses unless configured otherwise: 4 MiB.</summary>
        public const int DefaultMaxMessageBytes = 4 * 1024 * 1024;

        private const int ChunkSize = 16 * 1024;

        /// <summary>
        /// Receives one whole message, reassembled across frames. A peer close is acknowledged and returned as
        /// <see cref="WebSocketReceiveOutcome.Closed"/>; a message larger than <paramref name="maxMessageBytes"/> closes the
        /// socket with <see cref="WebSocketCloseStatus.MessageTooBig"/> and returns <see cref="WebSocketReceiveOutcome.TooBig"/>.
        /// A truncated message is never returned.
        /// </summary>
        public static async Task<WebSocketReceivedMessage> ReceiveMessageAsync(
            this SysWebSocket socket,
            int maxMessageBytes = DefaultMaxMessageBytes,
            CancellationToken cancellationToken = default)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            if (maxMessageBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxMessageBytes), maxMessageBytes, "Must be positive.");

            var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
            try
            {
                using var message = new MemoryStream();
                while (true)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(chunk), cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await TryCloseOutputAsync(socket, WebSocketCloseStatus.NormalClosure, null).ConfigureAwait(false);
                        return WebSocketReceivedMessage.ForClose(WebSocketReceiveOutcome.Closed, result.CloseStatus, result.CloseStatusDescription);
                    }

                    if (message.Length + result.Count > maxMessageBytes)
                    {
                        var description = $"Message exceeds {maxMessageBytes} bytes";
                        await TryCloseOutputAsync(socket, WebSocketCloseStatus.MessageTooBig, description).ConfigureAwait(false);
                        return WebSocketReceivedMessage.ForClose(WebSocketReceiveOutcome.TooBig, WebSocketCloseStatus.MessageTooBig, description);
                    }

                    message.Write(chunk, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        return WebSocketReceivedMessage.ForMessage(result.MessageType, message.ToArray());
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
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
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                // The peer is already gone; the outcome returned to the caller is unchanged.
            }
        }
    }
}
