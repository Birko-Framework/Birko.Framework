using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;
using Birko.Redis;
using StackExchange.Redis;

namespace Birko.MessageQueue.Redis
{
    /// <summary>
    /// Redis Streams message consumer. Uses XREADGROUP for consumer group subscriptions
    /// or XREAD for simple subscriptions. Supports manual and automatic acknowledgment via XACK.
    /// </summary>
    public class RedisConsumer : IMessageConsumer
    {
        private readonly RedisConnectionManager _connectionManager;
        private readonly IMessageSerializer _serializer;
        private readonly RedisStreamSettings _settings;
        private readonly ConcurrentDictionary<Guid, SubscriptionState> _subscriptions = new();
        private readonly ConcurrentDictionary<Guid, PendingMessage> _pendingAck = new();
        private bool _disposed;

        internal RedisConsumer(RedisConnectionManager connectionManager, IMessageSerializer serializer, RedisStreamSettings settings)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<ISubscription> SubscribeAsync(string destination, Func<QueueMessage, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (string.IsNullOrEmpty(destination))
            {
                throw new ArgumentException("Destination cannot be null or empty.", nameof(destination));
            }

            var opts = options ?? new ConsumerOptions();
            var streamKey = _settings.GetStreamKey(destination);
            var consumerGroup = opts.GroupId ?? _settings.ConsumerGroup;
            var consumerName = _settings.ConsumerName ?? $"consumer-{Guid.NewGuid():N}";

            var subscriptionId = Guid.NewGuid();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var state = new SubscriptionState
            {
                Destination = destination,
                StreamKey = streamKey,
                ConsumerGroup = consumerGroup,
                ConsumerName = consumerName,
                Handler = handler,
                Options = opts,
                Cts = cts
            };

            _subscriptions.TryAdd(subscriptionId, state);

            // Start polling loop
            _ = Task.Run(() => PollLoopAsync(subscriptionId, state), cts.Token);

            ISubscription subscription = new RedisSubscription(this, destination, subscriptionId);
            return subscription;
        }

        public Task<ISubscription> SubscribeAsync<T>(string destination, IMessageHandler<T> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default) where T : class
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return SubscribeAsync(destination, async (message, ct) =>
            {
                var payload = DeserializePayload<T>(message);
                if (payload != null)
                {
                    var context = new MessageContext(message, destination, this);
                    await handler.HandleAsync(payload, context, ct).ConfigureAwait(false);
                }
            }, options, cancellationToken);
        }

        public async Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_pendingAck.TryRemove(messageId, out var pending) && pending.ConsumerGroup != null)
            {
                var db = _connectionManager.GetDatabase();
                await db.StreamAcknowledgeAsync(pending.StreamKey, pending.ConsumerGroup, pending.StreamEntryId).ConfigureAwait(false);
            }
        }

        public Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Remove from pending; the message will be re-delivered by Redis
            // to another consumer in the group (via XCLAIM or pending entry list)
            _pendingAck.TryRemove(messageId, out _);
            return Task.CompletedTask;
        }

        internal void RemoveSubscription(Guid subscriptionId)
        {
            if (_subscriptions.TryRemove(subscriptionId, out var state))
            {
                state.Cts.Cancel();
                state.Cts.Dispose();
            }
        }

        private async Task PollLoopAsync(Guid subscriptionId, SubscriptionState state)
        {
            var db = _connectionManager.GetDatabase();
            var ct = state.Cts.Token;

            // Create consumer group if configured
            if (state.ConsumerGroup != null && _settings.AutoCreateConsumerGroup)
            {
                await EnsureConsumerGroupAsync(db, state.StreamKey, state.ConsumerGroup).ConfigureAwait(false);
            }

            // For consumer groups, start reading from ">" (new messages only),
            // unless FromBeginning is specified, in which case read from "0" first
            var position = state.Options.FromBeginning ? "0" : ">";
            var useConsumerGroup = state.ConsumerGroup != null;

            // If FromBeginning and using consumer group, first process pending entries
            if (useConsumerGroup && state.Options.FromBeginning)
            {
                await ProcessPendingEntriesAsync(db, state, ct).ConfigureAwait(false);
                position = ">";
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    StreamEntry[] entries;

                    if (useConsumerGroup)
                    {
                        entries = await db.StreamReadGroupAsync(
                            state.StreamKey,
                            state.ConsumerGroup!,
                            state.ConsumerName,
                            position,
                            count: _settings.ReadCount
                        ).ConfigureAwait(false);
                    }
                    else
                    {
                        // Simple XREAD - track last ID ourselves
                        var lastId = state.LastReadId ?? (state.Options.FromBeginning ? "0-0" : "$");
                        entries = await db.StreamReadAsync(
                            state.StreamKey,
                            lastId,
                            count: _settings.ReadCount
                        ).ConfigureAwait(false);
                    }

                    if (entries != null && entries.Length > 0)
                    {
                        foreach (var entry in entries)
                        {
                            if (ct.IsCancellationRequested)
                            {
                                break;
                            }

                            var message = ParseStreamEntry(entry);
                            if (message == null)
                            {
                                continue;
                            }

                            // Check TTL
                            var ttlField = entry.Values.FirstOrDefault(v => v.Name == "ttl_ms");
                            if (ttlField.Value.HasValue && long.TryParse(ttlField.Value.ToString(), out var ttlMs))
                            {
                                var elapsed = DateTimeOffset.UtcNow - message.CreatedAt;
                                if (elapsed.TotalMilliseconds > ttlMs)
                                {
                                    // Message expired, auto-ack and skip
                                    if (useConsumerGroup)
                                    {
                                        await db.StreamAcknowledgeAsync(state.StreamKey, state.ConsumerGroup!, entry.Id).ConfigureAwait(false);
                                    }
                                    continue;
                                }
                            }

                            if (state.Options.AckMode == MessageAckMode.ManualAck && useConsumerGroup)
                            {
                                _pendingAck.TryAdd(message.Id, new PendingMessage
                                {
                                    StreamKey = state.StreamKey,
                                    ConsumerGroup = state.ConsumerGroup,
                                    StreamEntryId = entry.Id
                                });
                            }

                            try
                            {
                                await state.Handler(message, ct).ConfigureAwait(false);

                                // Auto-ack on success
                                if (state.Options.AckMode == MessageAckMode.AutoAck && useConsumerGroup)
                                {
                                    await db.StreamAcknowledgeAsync(state.StreamKey, state.ConsumerGroup!, entry.Id).ConfigureAwait(false);
                                }
                            }
                            catch
                            {
                                if (state.Options.AckMode == MessageAckMode.ManualAck)
                                {
                                    _pendingAck.TryRemove(message.Id, out _);
                                }
                                // Message stays in pending entries list for consumer group re-delivery
                            }

                            if (!useConsumerGroup)
                            {
                                state.LastReadId = entry.Id;
                            }
                        }
                    }
                    else
                    {
                        // No messages available, wait before next poll
                        await Task.Delay(_settings.BlockMilliseconds ?? 1000, ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (RedisException)
                {
                    // Connection error; wait and retry
                    try
                    {
                        await Task.Delay(2000, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task ProcessPendingEntriesAsync(IDatabase db, SubscriptionState state, CancellationToken ct)
        {
            var startId = "0-0";
            while (!ct.IsCancellationRequested)
            {
                var entries = await db.StreamReadGroupAsync(
                    state.StreamKey,
                    state.ConsumerGroup!,
                    state.ConsumerName,
                    startId,
                    count: _settings.ReadCount
                ).ConfigureAwait(false);

                if (entries == null || entries.Length == 0)
                {
                    break;
                }

                foreach (var entry in entries)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    var message = ParseStreamEntry(entry);
                    if (message == null)
                    {
                        continue;
                    }

                    try
                    {
                        await state.Handler(message, ct).ConfigureAwait(false);

                        if (state.Options.AckMode == MessageAckMode.AutoAck)
                        {
                            await db.StreamAcknowledgeAsync(state.StreamKey, state.ConsumerGroup!, entry.Id).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        // Leave unacked for re-delivery
                    }
                }

                startId = entries[entries.Length - 1].Id;
            }
        }

        private QueueMessage? ParseStreamEntry(StreamEntry entry)
        {
            var values = entry.Values.ToDictionary(v => v.Name.ToString(), v => v.Value);

            // Try full message deserialization first
            if (values.TryGetValue("message", out var fullMessage) && fullMessage.HasValue)
            {
                try
                {
                    var message = _serializer.Deserialize<QueueMessage>(fullMessage.ToString());
                    return message;
                }
                catch
                {
                    // Fall through to field-by-field parsing
                }
            }

            // Field-by-field parsing
            if (!values.TryGetValue("body", out var body))
            {
                return null;
            }

            var queueMessage = new QueueMessage
            {
                Body = body.ToString()
            };

            if (values.TryGetValue("id", out var id) && Guid.TryParse(id.ToString(), out var messageId))
            {
                queueMessage.Id = messageId;
            }

            if (values.TryGetValue("payload_type", out var payloadType) && payloadType.HasValue && payloadType.ToString().Length > 0)
            {
                queueMessage.PayloadType = payloadType.ToString();
            }

            if (values.TryGetValue("headers", out var headers) && headers.HasValue)
            {
                try
                {
                    var parsedHeaders = _serializer.Deserialize<MessageHeaders>(headers.ToString());
                    if (parsedHeaders != null)
                    {
                        queueMessage.Headers = parsedHeaders;
                    }
                }
                catch
                {
                    // Use default headers
                }
            }

            if (values.TryGetValue("created_at", out var createdAt) && long.TryParse(createdAt.ToString(), out var ms))
            {
                queueMessage.CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }

            if (values.TryGetValue("priority", out var priority) && int.TryParse(priority.ToString(), out var prio))
            {
                queueMessage.Priority = prio;
            }

            return queueMessage;
        }

        private static async Task EnsureConsumerGroupAsync(IDatabase db, string streamKey, string groupName)
        {
            try
            {
                // Create the group starting from the beginning of the stream.
                // MKSTREAM creates the stream if it doesn't exist.
                await db.StreamCreateConsumerGroupAsync(streamKey, groupName, "0-0", createStream: true).ConfigureAwait(false);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                // Group already exists — this is fine
            }
        }

        private T? DeserializePayload<T>(QueueMessage message) where T : class
        {
            if (string.IsNullOrEmpty(message.Body))
            {
                return null;
            }

            return _serializer.Deserialize<T>(message.Body);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var kvp in _subscriptions)
            {
                kvp.Value.Cts.Cancel();
                kvp.Value.Cts.Dispose();
            }

            _subscriptions.Clear();
            _pendingAck.Clear();
        }

        private class SubscriptionState
        {
            public required string Destination { get; set; }
            public required string StreamKey { get; set; }
            public string? ConsumerGroup { get; set; }
            public required string ConsumerName { get; set; }
            public required Func<QueueMessage, CancellationToken, Task> Handler { get; set; }
            public required ConsumerOptions Options { get; set; }
            public required CancellationTokenSource Cts { get; set; }
            public string? LastReadId { get; set; }
        }

        private class PendingMessage
        {
            public required string StreamKey { get; set; }
            public string? ConsumerGroup { get; set; }
            public required RedisValue StreamEntryId { get; set; }
        }
    }
}
