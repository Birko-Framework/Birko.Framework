using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SysWebSocket = System.Net.WebSockets.WebSocket;

namespace Birko.Communication.WebSocket.Servers
{
    /// <summary>
    /// WebSocket server using HttpListener (standalone, not integrated with ASP.NET Core)
    /// For ASP.NET Core applications, use the Middleware extension methods instead.
    /// </summary>
    public class WebSocketServer : IDisposable
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<string, SysWebSocket> _clients = new();
        private readonly ILogger? _logger; // Optional: can be null if not using DI

        public event EventHandler<byte[]>? OnDataReceived;
        public event EventHandler<string>? OnClientConnected;
        public event EventHandler<string>? OnClientDisconnected;

        public bool IsListening => _listener != null && _listener.IsListening;

        public WebSocketServer(ILogger? logger = null)
        {
            _logger = logger;
        }

        public async Task StartAsync(string uriPrefix, CancellationToken cancellationToken = default)
        {
            if (_listener != null && _listener.IsListening)
                throw new InvalidOperationException("Server is already running");

            _listener = new HttpListener();
            _listener.Prefixes.Add(uriPrefix);
            _listener.Start();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _logger?.LogInformation("WebSocket Server started at {UriPrefix}", uriPrefix);

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);

                    // Process each request in a separate task
                    _ = Task.Run(() => ProcessRequestAsync(context, _cts.Token), _cts.Token);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown
            }
            catch (HttpListenerException)
            {
                // Listener stopped
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Server error");
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (_listener == null || !_listener.IsListening)
                return;

            _cts?.Cancel();

            // Close all clients gracefully
            var closeTasks = _clients.Values.Select(async client =>
            {
                try
                {
                    if (client.State == WebSocketState.Open)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", cts.Token)
                            .ConfigureAwait(false);
                    }
                    client.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error closing client WebSocket");
                }
            }).ToList();

            await Task.WhenAll(closeTasks).ConfigureAwait(false);
            _clients.Clear();

            _listener?.Stop();
            _listener?.Close();

            _cts?.Dispose();
            _cts = null;
        }

        public async Task BroadcastAsync(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var segment = new ArraySegment<byte>(data);
            var tasks = _clients.Values
                .Where(client => client.State == WebSocketState.Open)
                .Select(client => SendWithTimeoutAsync(client, segment, TimeSpan.FromSeconds(30)))
                .ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task SendWithTimeoutAsync(SysWebSocket webSocket, ArraySegment<byte> data, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await webSocket.SendAsync(data, WebSocketMessageType.Binary, true, cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("WebSocket send timeout");
                throw;
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            WebSocketContext? wsContext = null;
            try
            {
                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    return;
                }

                wsContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                string clientId = Guid.NewGuid().ToString();
                var socket = wsContext.WebSocket;

                _clients.TryAdd(clientId, socket);
                OnClientConnected?.Invoke(this, clientId);
                _logger?.LogInformation("Client connected: {ClientId}", clientId);

                await ReceiveLoopAsync(socket, clientId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing WebSocket request");
            }
        }

        private async Task ReceiveLoopAsync(SysWebSocket socket, string clientId, CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024]; // 64KB buffer for better performance
            var messageBuffer = new MemoryStream();

            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                        .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closed by client",
                            CancellationToken.None).ConfigureAwait(false);
                        break;
                    }

                    // Handle multi-frame messages
                    messageBuffer.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        var received = messageBuffer.ToArray();
                        messageBuffer.SetLength(0); // Reset for next message

                        OnDataReceived?.Invoke(this, received);
                        _logger?.LogDebug("Received {Count} bytes from {ClientId}", received.Length, clientId);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown
            }
            catch (WebSocketException ex)
            {
                _logger?.LogWarning(ex, "WebSocket connection lost for client {ClientId}", clientId);
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                OnClientDisconnected?.Invoke(this, clientId);
                _logger?.LogInformation("Client disconnected: {ClientId}", clientId);

                messageBuffer.Dispose();
                await DisposeAsync(socket).ConfigureAwait(false);
            }
        }

        private async Task DisposeAsync(SysWebSocket socket)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing",
                        cts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore errors during disposal
            }
            finally
            {
                socket.Dispose();
            }
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }
}
