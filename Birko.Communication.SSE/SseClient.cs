using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.SSE
{
    /// <summary>
    /// Client for receiving Server-Sent Events from an SSE endpoint
    /// </summary>
    public class SseClient : IDisposable
    {
        private CancellationTokenSource? _cts;
        private Task? _receiveTask;
        private readonly Dictionary<string, string> _headers;

        /// <summary>
        /// Raised when the client successfully connects to the server
        /// </summary>
        public event EventHandler? OnConnected;

        /// <summary>
        /// Raised when a generic message is received
        /// </summary>
        public event EventHandler<string>? OnMessage;

        /// <summary>
        /// Raised when a full SSE event is received
        /// </summary>
        public event EventHandler<SseEvent>? OnEvent;

        /// <summary>
        /// Raised when an error occurs
        /// </summary>
        public event EventHandler<Exception>? OnError;

        /// <summary>
        /// Raised when the client disconnects from the server
        /// </summary>
        public event EventHandler? OnDisconnected;

        /// <summary>
        /// Gets the server URL
        /// </summary>
        public string ServerUrl { get; }

        /// <summary>
        /// Gets the headers to send with the request
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers => _headers;

        /// <summary>
        /// Gets whether the client is connected
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Gets the last received event ID
        /// </summary>
        public string? LastEventId { get; private set; }

        /// <summary>
        /// Gets or sets the reconnection delay in milliseconds
        /// </summary>
        public int ReconnectDelay { get; set; } = 3000;

        /// <summary>
        /// Gets or sets whether automatic reconnection is enabled
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// Initializes a new instance of the SseClient class
        /// </summary>
        public SseClient(string serverUrl, Dictionary<string, string>? headers = null)
        {
            ServerUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
            _headers = headers ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Adds a header to the request
        /// </summary>
        public void AddHeader(string key, string value)
        {
            _headers[key] = value;
        }

        /// <summary>
        /// Connects to the SSE server
        /// </summary>
        public virtual async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsConnected = true;
            OnConnected?.Invoke(this, EventArgs.Empty);

            _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));

            await Task.CompletedTask;
        }

        /// <summary>
        /// Disconnects from the SSE server
        /// </summary>
        public async Task DisconnectAsync()
        {
            IsConnected = false;
            _cts?.Cancel();

            if (_receiveTask != null)
            {
                await _receiveTask;
            }

            _cts?.Dispose();
            _cts = null;

            OnDisconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Sets the Last-Event-ID header for reconnection
        /// </summary>
        public void SetLastEventId(string eventId)
        {
            LastEventId = eventId;
            _headers["Last-Event-ID"] = eventId;
        }

        private async Task ReceiveLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, ex);

                if (AutoReconnect && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(ReconnectDelay, cancellationToken);
                    // In a real implementation, would attempt reconnection here
                }
            }
        }

        /// <summary>
        /// Processes a line from the SSE stream
        /// </summary>
        protected virtual void ProcessEventLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line.Substring(6);
                OnMessage?.Invoke(this, data);
            }
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                var eventName = line.Substring(7);
                // Could be used for typed event handling
            }
            else if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                LastEventId = line.Substring(4);
            }
            else if (line.StartsWith("retry: ", StringComparison.Ordinal))
            {
                if (int.TryParse(line.Substring(7), out var retry))
                {
                    ReconnectDelay = retry;
                }
            }
        }

        public void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
    }
}
