using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private SseEvent _currentEvent = new();

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
        public SseClient(string serverUrl, Dictionary<string, string>? headers = null, HttpClient? httpClient = null)
        {
            ServerUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
            _headers = headers ?? new Dictionary<string, string>();
            // SSE holds the connection open indefinitely, so an owned client must not time out.
            // A caller-supplied client (e.g. a test with a mock handler) is used as-is.
            _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            _ownsHttpClient = httpClient == null;
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
            // Tear down any prior session first so a repeat call cannot orphan the previous
            // CancellationTokenSource and receive task (CR-M070).
            if (_cts != null)
            {
                await DisconnectAsync().ConfigureAwait(false);
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Actually open the stream so ConnectAsync completing means "connected" and failures
            // surface to the caller (CR-M071).
            var response = await OpenStreamAsync(_cts.Token).ConfigureAwait(false);
            IsConnected = true;
            OnConnected?.Invoke(this, EventArgs.Empty);

            _receiveTask = Task.Run(() => ReceiveLoop(response, _cts.Token));
        }

        private async Task<HttpResponseMessage> OpenStreamAsync(CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ServerUrl);
            request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
            foreach (var header in _headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (LastEventId != null)
                request.Headers.TryAddWithoutValidation("Last-Event-ID", LastEventId);

            var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return response;
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

        private async Task ReceiveLoop(HttpResponseMessage initialResponse, CancellationToken cancellationToken)
        {
            var response = initialResponse;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using (response)
                        using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                        using (var reader = new StreamReader(stream))
                        {
                            string? line;
                            while (!cancellationToken.IsCancellationRequested
                                && (line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
                            {
                                ProcessEventLine(line);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke(this, ex);
                    }

                    // Stream ended (or errored). Reconnect if configured — re-opening with the
                    // Last-Event-ID header so the server can resume (CR-M071).
                    if (!AutoReconnect || cancellationToken.IsCancellationRequested)
                        break;

                    await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                    response = await OpenStreamAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation / disconnect.
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, ex);
            }
            finally
            {
                IsConnected = false;
            }
        }

        /// <summary>
        /// Processes a line from the SSE stream
        /// </summary>
        protected virtual void ProcessEventLine(string line)
        {
            // Blank line = dispatch the accumulated event (SSE spec)
            if (string.IsNullOrEmpty(line))
            {
                if (_currentEvent.Data != null || _currentEvent.Event != null)
                {
                    if (_currentEvent.Data != null)
                    {
                        OnMessage?.Invoke(this, _currentEvent.Data);
                    }
                    OnEvent?.Invoke(this, _currentEvent);
                    _currentEvent = new SseEvent();
                }
                return;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line.Substring(6);
                _currentEvent.Data = _currentEvent.Data == null ? data : _currentEvent.Data + "\n" + data;
            }
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                _currentEvent.Event = line.Substring(7);
            }
            else if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                _currentEvent.Id = line.Substring(4);
                LastEventId = _currentEvent.Id;
            }
            else if (line.StartsWith("retry: ", StringComparison.Ordinal))
            {
                if (int.TryParse(line.Substring(7), out var retry))
                {
                    _currentEvent.Retry = retry;
                    ReconnectDelay = retry;
                }
            }
        }

        public void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
