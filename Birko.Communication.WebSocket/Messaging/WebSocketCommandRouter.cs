using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SysWebSocket = System.Net.WebSockets.WebSocket;

namespace Birko.Communication.WebSocket.Messaging
{
    /// <summary>
    /// Thrown by a command handler to fail the command with a message meant for the client. Its message is sent in the
    /// error reply; any other exception's message is not (see <see cref="WebSocketCommandRouterOptions.ExposeExceptionMessages"/>).
    /// </summary>
    public class WebSocketCommandException : Exception
    {
        public WebSocketCommandException(string message) : base(message) { }
        public WebSocketCommandException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>What a command handler receives about the request.</summary>
    public sealed class WebSocketCommandContext
    {
        internal WebSocketCommandContext(string command, JsonElement? data, JsonElement? id, SysWebSocket? socket)
        {
            Command = command;
            Data = data;
            Id = id;
            Socket = socket;
        }

        /// <summary>The command name as the client sent it.</summary>
        public string Command { get; }

        /// <summary>The request's <c>data</c>, or null when it sent none.</summary>
        public JsonElement? Data { get; }

        /// <summary>The request id, or null when it sent none.</summary>
        public JsonElement? Id { get; }

        /// <summary>The socket the request came on; null when the router was given text only.</summary>
        public SysWebSocket? Socket { get; }
    }

    /// <summary>Wire names and behaviour of a <see cref="WebSocketCommandRouter"/>.</summary>
    public sealed class WebSocketCommandRouterOptions
    {
        /// <summary>
        /// The request field naming the command. Default <c>type</c>: <c>WsClient.request(type, data)</c> in
        /// birko-web-core sends <c>{ type, id, data }</c>. A client that sends <c>{ command, id, data }</c> sets <c>command</c>.
        /// </summary>
        public string CommandField { get; set; } = "type";

        /// <summary>The field correlating a request and its reply, both ways (WsClient's <c>requestIdField</c>). Default <c>id</c>.</summary>
        public string IdField { get; set; } = "id";

        /// <summary>Whether command names match regardless of case. Default true.</summary>
        public bool IgnoreCommandCase { get; set; } = true;

        /// <summary>
        /// Whether an unexpected exception's message is sent to the client. Default false: it is logged, and the client
        /// gets <c>Command '{name}' failed.</c>, since an exception message can carry paths, SQL or other internals. A
        /// <see cref="WebSocketCommandException"/>'s message is always sent.
        /// </summary>
        public bool ExposeExceptionMessages { get; set; }

        /// <summary>Serializer options for typed <c>data</c> and for reply payloads. Default: camelCase, case-insensitive.</summary>
        public JsonSerializerOptions SerializerOptions { get; set; } = new(JsonSerializerDefaults.Web);
    }

    /// <summary>
    /// Routes <c>{ type, id, data }</c> requests to handlers by command name and replies
    /// <c>{ id, type: "response", data, timestamp }</c> or <c>{ id, type: "error", error, timestamp }</c>: the server half of
    /// birko-web-core's <c>WsClient.request</c>.
    /// </summary>
    /// <remarks>
    /// Every reply carries the request id when the request had one, including the error replies for an unknown command,
    /// unreadable <c>data</c> or a failing handler, so a correlating client fails at once instead of waiting out its timeout
    /// (DraCode's original dropped the id from every error). A request that is not JSON, or not a JSON object, has no id to
    /// echo and gets an error reply without one.
    /// </remarks>
    public sealed class WebSocketCommandRouter
    {
        private readonly Dictionary<string, Func<WebSocketCommandContext, CancellationToken, Task<object?>>> _handlers;
        private readonly WebSocketCommandRouterOptions _options;
        private readonly ILogger? _logger;

        public WebSocketCommandRouter(WebSocketCommandRouterOptions? options = null, ILogger? logger = null)
        {
            _options = options ?? new WebSocketCommandRouterOptions();
            if (string.IsNullOrEmpty(_options.CommandField)) throw new ArgumentException("CommandField is required.", nameof(options));
            if (string.IsNullOrEmpty(_options.IdField)) throw new ArgumentException("IdField is required.", nameof(options));
            if (_options.CommandField == _options.IdField) throw new ArgumentException("CommandField and IdField must differ.", nameof(options));
            _logger = logger;
            _handlers = new(_options.IgnoreCommandCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        /// <summary>Registers a handler that reads <see cref="WebSocketCommandContext.Data"/> itself. A name maps once.</summary>
        public WebSocketCommandRouter Map(string command, Func<WebSocketCommandContext, CancellationToken, Task<object?>> handler)
        {
            if (string.IsNullOrEmpty(command)) throw new ArgumentException("A command name is required.", nameof(command));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_handlers.TryAdd(command, handler))
                throw new ArgumentException($"The command '{command}' is already mapped.", nameof(command));
            return this;
        }

        /// <summary>
        /// Registers a handler whose <c>data</c> is deserialized to <typeparamref name="TData"/>. Data that does not
        /// deserialize is an error reply (with the request id); absent data is <c>default</c>.
        /// </summary>
        public WebSocketCommandRouter Map<TData>(string command, Func<TData?, WebSocketCommandContext, CancellationToken, Task<object?>> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return Map(command, (context, ct) =>
            {
                TData? data;
                try
                {
                    data = context.Data is { } element && element.ValueKind != JsonValueKind.Null
                        ? element.Deserialize<TData>(_options.SerializerOptions)
                        : default;
                }
                catch (JsonException ex)
                {
                    throw new WebSocketCommandException($"Invalid data for command '{context.Command}': {ex.Message}", ex);
                }
                return handler(data, context, ct);
            });
        }

        /// <summary>Handles one request and returns the reply's JSON. Never throws for a bad request; cancellation propagates.</summary>
        public Task<string> HandleAsync(string message, CancellationToken cancellationToken = default)
            => HandleAsync(message, socket: null, cancellationToken);

        /// <summary>Handles one request received on <paramref name="socket"/> and sends the reply while the socket is open.</summary>
        public async Task HandleAsync(SysWebSocket socket, string message, CancellationToken cancellationToken = default)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            var reply = await HandleAsync(message, socket, cancellationToken).ConfigureAwait(false);
            if (socket.State != WebSocketState.Open) return;
            // Concurrent sends on one socket are serialized by the runtime (measured at TASK-537): no send queue.
            await socket.SendAsync(Encoding.UTF8.GetBytes(reply), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads requests from <paramref name="socket"/> until it closes, and answers each one. Messages are read whole
        /// with <see cref="WebSocketMessageExtensions.ReceiveMessageAsync"/> (a larger one closes the socket with
        /// MessageTooBig). Requests are handled concurrently, so a slow command does not hold up the next; the method
        /// returns once the socket is closed and every started request has replied. A binary message gets an error reply.
        /// </summary>
        public async Task RunAsync(SysWebSocket socket, int maxMessageBytes = WebSocketMessageExtensions.DefaultMaxMessageBytes, CancellationToken cancellationToken = default)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            var running = new List<Task>();
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var received = await socket.ReceiveMessageAsync(maxMessageBytes, cancellationToken).ConfigureAwait(false);
                    if (!received.IsMessage) break;
                    running.RemoveAll(t => t.IsCompleted);
                    running.Add(received.MessageType == WebSocketMessageType.Text
                        ? HandleSafelyAsync(socket, received.GetText(), cancellationToken)
                        : SendSafelyAsync(socket, ErrorReply(null, "Binary messages are not supported."), cancellationToken));
                }
            }
            finally
            {
                await Task.WhenAll(running).ConfigureAwait(false);
            }
        }

        private async Task HandleSafelyAsync(SysWebSocket socket, string message, CancellationToken ct)
        {
            try
            {
                await HandleAsync(socket, message, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                // The connection went away or the run was cancelled: there is no one to reply to.
                _logger?.LogDebug(ex, "WebSocket command reply not sent");
            }
        }

        private async Task SendSafelyAsync(SysWebSocket socket, string reply, CancellationToken ct)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.SendAsync(Encoding.UTF8.GetBytes(reply), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                _logger?.LogDebug(ex, "WebSocket command reply not sent");
            }
        }

        private async Task<string> HandleAsync(string message, SysWebSocket? socket, CancellationToken ct)
        {
            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(message ?? string.Empty);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return ErrorReply(null, "The message is not valid JSON.");
            }
            if (root.ValueKind != JsonValueKind.Object)
                return ErrorReply(null, "The message must be a JSON object.");

            JsonElement? id = root.TryGetProperty(_options.IdField, out var idElement) && idElement.ValueKind != JsonValueKind.Null
                ? idElement
                : null;
            if (!root.TryGetProperty(_options.CommandField, out var commandElement)
                || commandElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(commandElement.GetString()))
            {
                return ErrorReply(id, $"The message has no '{_options.CommandField}'.");
            }

            var command = commandElement.GetString()!;
            if (!_handlers.TryGetValue(command, out var handler))
                return ErrorReply(id, $"Unknown command: {command}");

            JsonElement? data = root.TryGetProperty("data", out var dataElement) ? dataElement : null;
            try
            {
                var result = await handler(new WebSocketCommandContext(command, data, id, socket), ct).ConfigureAwait(false);
                // Serialized inside the try: a result the serializer refuses (a cycle, an unsupported type) is the
                // command failing, and gets an error reply with the id like any other failure.
                return Reply(id, "response", "data", result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (WebSocketCommandException ex)
            {
                return ErrorReply(id, ex.Message);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "WebSocket command {Command} failed", command);
                return ErrorReply(id, _options.ExposeExceptionMessages ? ex.Message : $"Command '{command}' failed.");
            }
        }

        private string ErrorReply(JsonElement? id, string error) => Reply(id, "error", "error", error);

        private string Reply(JsonElement? id, string type, string payloadField, object? payload)
        {
            var buffer = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                if (id is { } value)
                {
                    writer.WritePropertyName(_options.IdField);
                    value.WriteTo(writer);
                }
                writer.WriteString("type", type);
                writer.WritePropertyName(payloadField);
                JsonSerializer.Serialize(writer, payload, payload?.GetType() ?? typeof(object), _options.SerializerOptions);
                writer.WriteString("timestamp", DateTime.UtcNow);
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
