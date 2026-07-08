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
        private readonly Func<IDatabase> _databaseFactory;
        private readonly IMessageSerializer _serializer;
        private readonly RedisStreamSettings _settings;
        private readonly ConcurrentDictionary<Guid, SubscriptionState> _subscriptions = new();
        private readonly ConcurrentDictionary<Guid, PendingMessage> _pendingAck = new();
        private bool _disposed;

        internal RedisConsumer(RedisConnectionManager connectionManager, IMessageSerializer serializer, RedisStreamSettings settings)
        {
            if (connectionManager == null) throw new ArgumentNullException(nameof(connectionManager));
            _databaseFactory = connectionManager.GetDatabase;
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Test seam: constructs the consumer over a supplied <see cref="IDatabase"/> factory
        /// instead of a live connection, so the poll / reclaim / ack paths can be exercised
        /// against a mocked database.
        /// </summary>
        internal RedisConsumer(Func<IDatabase> databaseFactory, IMessageSerializer serializer, RedisStreamSettings settings)
        {
            _databaseFactory = databaseFactory ?? throw new ArgumentNullException(nameof(databaseFactory));
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
                var db = _databaseFactory();
                await db.StreamAcknowledgeAsync(pending.StreamKey, pending.ConsumerGroup, pending.StreamEntryId).ConfigureAwait(false);
            }
        }

        public async Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_pendingAck.TryRemove(messageId, out var pending) || pending.ConsumerGroup == null)
            {
                return;
            }

            if (requeue)
            {
                // Leave the entry unacknowledged in the Pending Entries List; the reclaim pass
                // (XAUTOCLAIM, gated by PendingRetryMilliseconds) re-delivers it. Dropping only the
                // local tracker — as the old code did unconditionally — was a silent no-op.
                return;
            }

            // No requeue: acknowledge the entry so it leaves the PEL and is not re-delivered.
            // (There is no dead-letter stream, so this discards the message.)
            var db = _databaseFactory();
            await db.StreamAcknowledgeAsync(pending.StreamKey, pending.ConsumerGroup, pending.StreamEntryId).ConfigureAwait(false);
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
            var db = _databaseFactory();
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

                            await ProcessEntryAsync(db, state, entry, useConsumerGroup, ct).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        // No new messages. Reclaim idle pending entries so messages that failed
                        // under ManualAck or were RejectAsync(requeue:true)'d are actually
                        // re-delivered during the subscription lifetime (XAUTOCLAIM over the PEL),
                        // rather than sitting unacked until a fresh FromBeginning subscription.
                        if (useConsumerGroup && _settings.PendingRetryMilliseconds > 0)
                        {
                            await ReclaimPendingEntriesAsync(db, state, ct).ConfigureAwait(false);
                        }

                        // Wait before next poll
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

        /// <summary>
        /// Processes a single stream entry: TTL check, optional manual-ack tracking, handler
        /// invocation and auto-ack. On handler failure the entry is left unacked in the Pending
        /// Entries List so it can be reclaimed/redelivered. Shared by the live poll and the
        /// pending-entry reclaim path so both behave identically.
        /// </summary>
        private async Task ProcessEntryAsync(IDatabase db, SubscriptionState state, StreamEntry entry, bool useConsumerGroup, CancellationToken ct)
        {
            var message = ParseStreamEntry(entry);
            if (message == null)
            {
                return;
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
                    return;
                }
            }

            if (state.Options.AckMode == MessageAckMode.ManualAck && useConsumerGroup)
            {
                _pendingAck[message.Id] = new PendingMessage
                {
                    StreamKey = state.StreamKey,
                    ConsumerGroup = state.ConsumerGroup,
                    StreamEntryId = entry.Id
                };
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
                // Entry stays in the Pending Entries List; the reclaim pass will redeliver it.
            }

            if (!useConsumerGroup)
            {
                state.LastReadId = entry.Id;
            }
        }

        /// <summary>
        /// Reclaims pending (delivered-but-unacknowledged) entries that have been idle at least
        /// <see cref="RedisStreamSettings.PendingRetryMilliseconds"/> via XAUTOCLAIM and re-processes
        /// them. This is what actually re-delivers failed / requeued messages (and messages orphaned
        /// by a crashed consumer) during the lifetime of the subscription.
        /// </summary>
        private async Task ReclaimPendingEntriesAsync(IDatabase db, SubscriptionState state, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var result = await db.StreamAutoClaimAsync(
                state.StreamKey,
                state.ConsumerGroup!,
                state.ConsumerName,
                _settings.PendingRetryMilliseconds,
                "0-0",
                count: _settings.ReadCount).ConfigureAwait(false);

            if (result.IsNull || result.ClaimedEntries == null || result.ClaimedEntries.Length == 0)
            {
                return;
            }

            foreach (var entry in result.ClaimedEntries)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                await ProcessEntryAsync(db, state, entry, useConsumerGroup: true, ct).ConfigureAwait(false);
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

                    await ProcessEntryAsync(db, state, entry, useConsumerGroup: true, ct).ConfigureAwait(false);
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
