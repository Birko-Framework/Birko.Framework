using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.GraphQL
{
    /// <summary>
    /// Reads a complete WebSocket message, accumulating fragments until EndOfMessage before
    /// returning (CR-H021). A single fixed-buffer ReceiveAsync assumed every message fit in one
    /// &lt;= 8 KB frame, so any larger or server-fragmented payload was parsed as truncated JSON.
    /// </summary>
    internal static class WebSocketMessageReader
    {
        public static async Task<WsMessage> ReceiveTextAsync(WebSocket socket, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return new WsMessage(WebSocketMessageType.Close, null);
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var text = result.MessageType == WebSocketMessageType.Text
                ? Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length)
                : null;
            return new WsMessage(result.MessageType, text);
        }
    }

    internal readonly struct WsMessage
    {
        public WsMessage(WebSocketMessageType type, string? text)
        {
            Type = type;
            Text = text;
        }

        public WebSocketMessageType Type { get; }
        public string? Text { get; }
    }
}
