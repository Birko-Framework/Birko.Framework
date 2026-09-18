using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.GraphQL;

/// <summary>
/// Handle for a GraphQL subscription. Implements <see cref="IObservable{T}"/>
/// for pushing received data to subscribers.
/// </summary>
public interface IGraphQLSubscription<T> : IDisposable
{
    /// <summary>
    /// Returns the underlying <see cref="IObservable{T}"/> for attaching subscribers.
    /// </summary>
    IObservable<T> AsObservable();

    /// <summary>
    /// Sends a stop message to the server and completes the subscription.
    /// </summary>
    Task UnsubscribeAsync(CancellationToken ct = default);
}

/// <summary>
/// WebSocket-based GraphQL subscription using the graphql-ws protocol.
/// </summary>
internal class GraphQLSubscription<T> : IGraphQLSubscription<T>, IObservable<T>
{
    private readonly ClientWebSocket _webSocket;
    private readonly string _subscriptionId;
    private readonly CancellationTokenSource _cts;
    private readonly List<IObserver<T>> _observers = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private bool _completed;

    public GraphQLSubscription(ClientWebSocket webSocket, string subscriptionId, CancellationTokenSource cts)
    {
        _webSocket = webSocket;
        _subscriptionId = subscriptionId;
        _cts = cts;
    }

    public IObservable<T> AsObservable() => this;

    public async Task UnsubscribeAsync(CancellationToken ct = default)
    {
        if (_completed) return;

        // graphql-transport-ws terminates a subscription with "complete" (not the legacy "stop") — CR-H020.
        var stopMsg = JsonSerializer.Serialize(new
        {
            type = "complete",
            id = _subscriptionId
        }, _jsonOptions);

        var bytes = Encoding.UTF8.GetBytes(stopMsg);
        if (_webSocket.State == WebSocketState.Open)
        {
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        Complete();
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();

        Complete();

        if (_webSocket.State != WebSocketState.None)
        {
            _webSocket.Dispose();
        }

        _lock.Dispose();
        _cts.Dispose();
    }

    /// <summary>
    /// Starts the background receive loop. Called once after the subscription is started.
    /// </summary>
    internal async Task StartReceivingAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                // Accumulate fragmented/large frames until complete before parsing (CR-H021).
                var message = await WebSocketMessageReader.ReceiveTextAsync(_webSocket, _cts.Token).ConfigureAwait(false);

                if (message.Type == WebSocketMessageType.Close)
                {
                    Complete();
                    return;
                }

                if (message.Text == null) continue;

                if (await HandleMessageAsync(message.Text).ConfigureAwait(false))
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose
        }
        catch (WebSocketException)
        {
            // Connection lost
        }
        finally
        {
            Complete();
        }
    }

    /// <summary>
    /// Parses one graphql-transport-ws frame and dispatches it to the observers: <c>data</c>/<c>next</c>
    /// → <see cref="IObserver{T}.OnNext"/> (and OnError for a payload-errors frame), <c>complete</c> →
    /// completion, <c>error</c> → OnError + completion. Frames whose <c>id</c> is not this subscription
    /// (except <c>complete</c>/<c>connection_error</c>) are ignored. Returns <c>true</c> when the
    /// subscription is terminated and the receive loop should stop. Extracted from the receive loop so
    /// this dispatch + id-filtering is unit-testable without a live WebSocket (CR-M046).
    /// </summary>
    internal async Task<bool> HandleMessageAsync(string messageText)
    {
        using var doc = JsonDocument.Parse(messageText);
        var root = doc.RootElement;

        var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

        if (id != _subscriptionId && type != "complete" && type != "connection_error") return false;

        switch (type)
        {
            case "data" or "next":
                if (root.TryGetProperty("payload", out var payload)
                    && payload.TryGetProperty("data", out var dataEl))
                {
                    var data = dataEl.Deserialize<T>(_jsonOptions);
                    if (data is not null)
                    {
                        await _lock.WaitAsync(_cts.Token).ConfigureAwait(false);
                        try
                        {
                            foreach (var observer in _observers)
                                observer.OnNext(data);
                        }
                        finally
                        {
                            _lock.Release();
                        }
                    }
                }

                if (root.TryGetProperty("payload", out var errPayload)
                    && errPayload.TryGetProperty("errors", out var errorsEl))
                {
                    var errors = errorsEl.Deserialize<List<GraphQLError>>(_jsonOptions);
                    if (errors is not null)
                    {
                        await _lock.WaitAsync(_cts.Token).ConfigureAwait(false);
                        try
                        {
                            var ex = new GraphQLException("Subscription error", errors);
                            foreach (var observer in _observers)
                                observer.OnError(ex);
                        }
                        finally
                        {
                            _lock.Release();
                        }
                    }
                }

                return false;

            case "complete":
                Complete();
                return true;

            case "error":
                var gqlErrors = root.TryGetProperty("payload", out var errPayload2)
                    ? errPayload2.Deserialize<List<GraphQLError>>(_jsonOptions)
                    : null;
                var error = new GraphQLException("Subscription error", gqlErrors ?? []);
                await _lock.WaitAsync(_cts.Token).ConfigureAwait(false);
                try
                {
                    foreach (var observer in _observers)
                        observer.OnError(error);
                }
                finally
                {
                    _lock.Release();
                }

                Complete();
                return true;
        }

        return false;
    }

    private void Complete()
    {
        if (_completed) return;
        _completed = true;

        _lock.Wait();
        try
        {
            foreach (var observer in _observers)
                observer.OnCompleted();
            _observers.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    IDisposable IObservable<T>.Subscribe(IObserver<T> observer)
    {
        _lock.Wait();
        try
        {
            _observers.Add(observer);
        }
        finally
        {
            _lock.Release();
        }

        return new Unsubscriber(_observers, observer, _lock);
    }

    private sealed class Unsubscriber(List<IObserver<T>> observers, IObserver<T> observer, SemaphoreSlim lockObj) : IDisposable
    {
        public void Dispose()
        {
            lockObj.Wait();
            try
            {
                observers.Remove(observer);
            }
            finally
            {
                lockObj.Release();
            }
        }
    }
}
