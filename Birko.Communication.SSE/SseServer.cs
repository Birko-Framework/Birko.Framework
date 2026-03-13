using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.SSE
{
    /// <summary>
    /// Server-Sent Events (SSE) server for pushing real-time events to connected clients
    /// </summary>
    public class SseServer : IDisposable
    {
        private readonly ConcurrentDictionary<string, ISseClientConnection> _clients = new();
        private CancellationTokenSource? _serverCts;
        private long _lastEventId;
        private bool _isListening;
        private readonly object _lock = new();

        /// <summary>
        /// Raised when a new client connects
        /// </summary>
        public event EventHandler<ISseClientConnection>? OnClientConnected;

        /// <summary>
        /// Raised when a client disconnects
        /// </summary>
        public event EventHandler<ISseClientConnection>? OnClientDisconnected;

        /// <summary>
        /// Raised when an event is sent to a client
        /// </summary>
        public event EventHandler<(ISseClientConnection client, SseEvent sseEvent)>? OnEventSent;

        /// <summary>
        /// Gets whether the server is currently listening for connections
        /// </summary>
        public bool IsListening => _isListening;

        /// <summary>
        /// Gets the number of currently connected clients
        /// </summary>
        public int ConnectedClientCount => _clients.Count;

        /// <summary>
        /// Gets all connected client IDs
        /// </summary>
        public IEnumerable<string> ConnectedClientIds => _clients.Keys;

        /// <summary>
        /// Initializes a new instance of the SseServer class
        /// </summary>
        public SseServer()
        {
        }

        /// <summary>
        /// Starts the SSE server
        /// </summary>
        /// <param name="uriPrefix">The URI prefix to listen on (e.g., "http://localhost:8080/sse/")</param>
        /// <param name="cancellationToken">Cancellation token for stopping the server</param>
        public virtual async Task StartAsync(string uriPrefix, CancellationToken cancellationToken = default)
        {
            _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isListening = true;

            await Task.CompletedTask;
        }

        /// <summary>
        /// Stops the SSE server and disconnects all clients
        /// </summary>
        public async Task StopAsync()
        {
            _isListening = false;

            // Disconnect all clients
            foreach (var client in _clients.Values)
            {
                client.Disconnect();
            }
            _clients.Clear();

            _serverCts?.Cancel();
            _serverCts?.Dispose();
            _serverCts = null;

            await Task.CompletedTask;
        }

        /// <summary>
        /// Broadcasts an event to all connected clients
        /// </summary>
        public async Task BroadcastAsync(SseEvent sseEvent)
        {
            foreach (var client in _clients.Values)
            {
                await client.SendAsync(sseEvent);
                OnEventSent?.Invoke(this, (client, sseEvent));
            }
        }

        /// <summary>
        /// Broadcasts data to all connected clients
        /// </summary>
        public async Task BroadcastAsync(string data, string? @event = null, string? id = null)
        {
            await BroadcastAsync(SseEvent.Create(data, @event, id ?? GenerateEventId()));
        }

        /// <summary>
        /// Broadcasts JSON data to all connected clients
        /// </summary>
        public async Task BroadcastJsonAsync<T>(T data, string? @event = null, string? id = null)
        {
            await BroadcastAsync(SseEvent.FromJson(data, @event, id ?? GenerateEventId()));
        }

        /// <summary>
        /// Sends an event to a specific client
        /// </summary>
        public async Task<bool> SendToClientAsync(string clientId, SseEvent sseEvent)
        {
            if (_clients.TryGetValue(clientId, out var client))
            {
                await client.SendAsync(sseEvent);
                OnEventSent?.Invoke(this, (client, sseEvent));
                return true;
            }
            return false;
        }

        /// <summary>
        /// Sends data to a specific client
        /// </summary>
        public async Task<bool> SendToClientAsync(string clientId, string data, string? @event = null, string? id = null)
        {
            return await SendToClientAsync(clientId, SseEvent.Create(data, @event, id ?? GenerateEventId()));
        }

        /// <summary>
        /// Sends JSON data to a specific client
        /// </summary>
        public async Task<bool> SendJsonToClientAsync<T>(string clientId, T data, string? @event = null, string? id = null)
        {
            return await SendToClientAsync(clientId, SseEvent.FromJson(data, @event, id ?? GenerateEventId()));
        }

        /// <summary>
        /// Registers a new client connection
        /// </summary>
        public async Task<ISseClientConnection> RegisterClientAsync(string? remoteEndPoint = null, Dictionary<string, string>? headers = null)
        {
            var clientId = GenerateClientId();
            var clientConnection = new SseClientConnection(clientId, remoteEndPoint, headers);

            clientConnection.OnDisconnected += (sender, client) =>
            {
                _ = Task.Run(() =>
                {
                    if (_clients.TryRemove(client.ClientId, out var removed))
                    {
                        OnClientDisconnected?.Invoke(this, removed);
                    }
                });
            };

            _clients[clientId] = clientConnection;
            OnClientConnected?.Invoke(this, clientConnection);

            return clientConnection;
        }

        /// <summary>
        /// Gets a client by ID
        /// </summary>
        public ISseClientConnection? GetClient(string clientId)
        {
            return _clients.TryGetValue(clientId, out var client) ? client : null;
        }

        /// <summary>
        /// Disconnects a specific client
        /// </summary>
        public bool DisconnectClient(string clientId)
        {
            if (_clients.TryGetValue(clientId, out var client))
            {
                client.Disconnect();
                return true;
            }
            return false;
        }

        private string GenerateEventId()
        {
            return Interlocked.Increment(ref _lastEventId).ToString();
        }

        private string GenerateClientId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public virtual void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Interface for SSE client connections
    /// </summary>
    public interface ISseClientConnection : IDisposable
    {
        /// <summary>
        /// Gets the unique client identifier
        /// </summary>
        string ClientId { get; }

        /// <summary>
        /// Gets the remote endpoint address
        /// </summary>
        string? RemoteEndPoint { get; }

        /// <summary>
        /// Gets when the client connected
        /// </summary>
        DateTime ConnectedAt { get; }

        /// <summary>
        /// Gets the request headers
        /// </summary>
        Dictionary<string, string> Headers { get; }

        /// <summary>
        /// Gets whether the client is still connected
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Raised when the client disconnects
        /// </summary>
        event EventHandler<ISseClientConnection>? OnDisconnected;

        /// <summary>
        /// Sends an SSE event to this client
        /// </summary>
        Task SendAsync(SseEvent sseEvent);

        /// <summary>
        /// Sends data to this client
        /// </summary>
        Task SendAsync(string data, string? @event = null, string? id = null);

        /// <summary>
        /// Sends JSON data to this client
        /// </summary>
        Task SendJsonAsync<T>(T data, string? @event = null, string? id = null);

        /// <summary>
        /// Disconnects the client
        /// </summary>
        void Disconnect();
    }

    /// <summary>
    /// Default implementation of SSE client connection
    /// </summary>
    public class SseClientConnection : ISseClientConnection
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _sendTask;
        private readonly Queue<SseEvent> _eventQueue = new();
        private readonly SemaphoreSlim _queueLock = new(1, 1);

        public string ClientId { get; }
        public string? RemoteEndPoint { get; }
        public DateTime ConnectedAt { get; }
        public Dictionary<string, string> Headers { get; }
        public bool IsConnected { get; private set; }

        public event EventHandler<ISseClientConnection>? OnDisconnected;

        public SseClientConnection(string clientId, string? remoteEndPoint = null, Dictionary<string, string>? headers = null)
        {
            ClientId = clientId;
            RemoteEndPoint = remoteEndPoint;
            ConnectedAt = DateTime.UtcNow;
            Headers = headers ?? new Dictionary<string, string>();
            IsConnected = true;

            _sendTask = Task.Run(SendLoop);
        }

        public async Task SendAsync(SseEvent sseEvent)
        {
            await _queueLock.WaitAsync();
            try
            {
                if (IsConnected)
                {
                    _eventQueue.Enqueue(sseEvent);
                }
            }
            finally
            {
                _queueLock.Release();
            }
        }

        public async Task SendAsync(string data, string? @event = null, string? id = null)
        {
            await SendAsync(SseEvent.Create(data, @event, id));
        }

        public async Task SendJsonAsync<T>(T data, string? @event = null, string? id = null)
        {
            await SendAsync(SseEvent.FromJson(data, @event, id));
        }

        private async Task SendLoop()
        {
            while (!_cts.IsCancellationRequested && IsConnected)
            {
                SseEvent? sseEvent = null;

                await _queueLock.WaitAsync(_cts.Token);
                try
                {
                    if (_eventQueue.Count > 0)
                    {
                        sseEvent = _eventQueue.Dequeue();
                    }
                }
                finally
                {
                    _queueLock.Release();
                }

                if (sseEvent != null)
                {
                    try
                    {
                        await OnSendAsync(sseEvent.ToString());
                    }
                    catch when (!_cts.IsCancellationRequested)
                    {
                        Disconnect();
                        break;
                    }
                }
                else
                {
                    await Task.Delay(100, _cts.Token);
                }
            }
        }

        protected virtual Task OnSendAsync(string formattedEvent)
        {
            // Override in actual implementation to write to network stream
            return Task.CompletedTask;
        }

        public void Disconnect()
        {
            if (IsConnected)
            {
                IsConnected = false;
                OnDisconnected?.Invoke(this, this);
            }
        }

        public void Dispose()
        {
            Disconnect();
            _cts.Cancel();
            _sendTask.Wait(TimeSpan.FromSeconds(5));
            _cts.Dispose();
            _queueLock.Dispose();
        }
    }
}
