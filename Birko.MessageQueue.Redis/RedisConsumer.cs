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

        /// <summary>
        /// Raised when a subscription's background poll loop hits a non-cancellation error (CR-M207).
        /// A transient per-iteration fault is reported and the loop keeps polling; a fault that
        /// terminates the loop is reported and the subscription is removed (so its <see
        /// cref="RedisSubscription.IsActive"/> flips to false) instead of appearing alive but dead.
        /// </summary>
        internal event EventHandler<Exception>? PollError;

        /// <summary>
        /// True while the given subscription is still registered (its poll loop has not terminated) and
        /// the consumer is not disposed. Backs <see cref="RedisSubscription.IsActive"/>.
        /// </summary>
        internal bool IsSubscriptionActive(Guid subscriptionId)
            => !_disposed && _subscriptions.ContainsKey(subscriptionId);

        /// <summary>
        /// Invoked when a message handler throws (SH-M100), so a failure is observable instead of lost silently.
        /// With a consumer group the entry stays in the Pending Entries List and is redelivered by the reclaim pass;
        /// without one there is no PEL, the read position moves on, and delivery is at-most-once — this hook is the
        /// only trace. An exception thrown by the hook is swallowed.
        /// </summary>
        public Func<Exception, QueueMessage, Task>? OnHandlerError { get; set; }

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

            var subscriptionId = Guid.NewGuid();
            // CR-M208: a per-subscription suffix keeps each subscription's consumer identity distinct
            // within the group even when a fixed ConsumerName is configured — otherwise two subscriptions
            // on the same destination would share one PEL, so XACK/redelivery couldn't tell them apart.
            var consumerName = _settings.ConsumerName is { } configuredName
                ? $"{configuredName}-{subscriptionId:N}"
                : $"consumer-{subscriptionId:N}";
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
            cancellationToken.ThrowIfCancellationRequested(); // CR-M206

            if (_pendingAck.TryRemove(messageId, out var pending) && pending.ConsumerGroup != null)
            {
                var db = _databaseFactory();
                await db.StreamAcknowledgeAsync(pending.StreamKey, pending.ConsumerGroup, pending.EntryIds()).ConfigureAwait(false);
            }
        }

        public async Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested(); // CR-M206

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
            await db.StreamAcknowledgeAsync(pending.StreamKey, pending.ConsumerGroup, pending.EntryIds()).ConfigureAwait(false);
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
            var ct = state.Cts.Token;
            try
            {
                await RunPollLoopAsync(state, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown (Unsubscribe/Dispose cancels the CTS).
            }
            catch (Exception ex)
            {
                // CR-M207: the loop (or its pre-loop setup, e.g. EnsureConsumerGroupAsync) terminated on
                // an unexpected fault. Surface it and remove the subscription so it stops reporting
                // IsActive == true while polling nothing.
                PollError?.Invoke(this, ex);
                RemoveSubscription(subscriptionId);
            }
        }

        private async Task RunPollLoopAsync(SubscriptionState state, CancellationToken ct)
        {
            var db = _databaseFactory();

            // For consumer groups, start reading from ">" (new messages only),
            // unless FromBeginning is specified, in which case read from "0" first
            var position = state.Options.FromBeginning ? "0" : ">";
            var useConsumerGroup = state.ConsumerGroup != null;

            // SH-M103: the setup is retried on a Redis error, as every poll is. It ran outside the loop's retry, so a
            // connection blip at subscribe time removed the subscription for good while the same blip a moment later
            // was retried. Any other setup fault still ends the loop (CR-M207).
            while (true)
            {
                try
                {
                    if (state.ConsumerGroup != null && _settings.AutoCreateConsumerGroup)
                    {
                        await EnsureConsumerGroupAsync(db, state.StreamKey, state.ConsumerGroup, state.Options.FromBeginning).ConfigureAwait(false);
                    }

                    // If FromBeginning and using consumer group, first process pending entries
                    if (useConsumerGroup && state.Options.FromBeginning)
                    {
                        await ProcessPendingEntriesAsync(db, state, ct).ConfigureAwait(false);
                        position = ">";
                    }
                    break;
                }
                catch (RedisException)
                {
                    await Task.Delay(2000, ct).ConfigureAwait(false);
                }
            }

            var nextReclaimAt = Environment.TickCount64 + _settings.PendingRetryMilliseconds;

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
                        // Simple XREAD - track last ID ourselves. "New messages only" starts from the stream's
                        // current tail as a concrete id: XREAD's "$" means "after whatever is last at the moment
                        // of this call", so even a blocking client would skip everything between polls, and
                        // StackExchange.Redis refuses it outright (InvalidOperationException) — every poll threw
                        // and nothing was ever delivered (SH-H020). Resolved inside the try so a connection error
                        // at startup is retried like any other.
                        state.LastReadId ??= state.Options.FromBeginning
                            ? "0-0"
                            : await ResolveStreamTailAsync(db, state.StreamKey).ConfigureAwait(false);
                        entries = await db.StreamReadAsync(
                            state.StreamKey,
                            state.LastReadId,
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

                        // SH-M101: the reclaim also runs on a busy stream, once per PendingRetryMilliseconds. It ran
                        // only after an empty poll, so on a stream that always had entries a failed or requeued entry
                        // was never redelivered.
                        if (useConsumerGroup && _settings.PendingRetryMilliseconds > 0 && Environment.TickCount64 >= nextReclaimAt)
                        {
                            nextReclaimAt = Environment.TickCount64 + _settings.PendingRetryMilliseconds;
                            await ReclaimPendingEntriesAsync(db, state, ct).ConfigureAwait(false);
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
                catch (Exception ex)
                {
                    // CR-M207: a non-cancellation, non-Redis per-iteration fault (serializer error,
                    // NRE, …) must not silently kill the loop. Surface it and keep polling after a
                    // backoff, so one poisoned entry doesn't stop the subscription forever.
                    PollError?.Invoke(this, ex);
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
            // Without a group the read position is ours, and it moves past every entry the read returned —
            // including one that is unparseable or expired. Advancing only after a delivered entry re-read such
            // an entry on every poll, with no delay because the batch was non-empty (SH-H020).
            if (!useConsumerGroup)
            {
                state.LastReadId = entry.Id;
            }

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
                // SH-M104: a message id delivered as more than one entry (a duplicate publish) keeps every entry id,
                // so acknowledging the id acknowledges them all. The assignment replaced the earlier entry's id,
                // which then sat in the PEL forever.
                _pendingAck.AddOrUpdate(message.Guid,
                    _ => new PendingMessage { StreamKey = state.StreamKey, ConsumerGroup = state.ConsumerGroup, StreamEntryIds = { entry.Id } },
                    (_, existing) => { existing.Add(entry.Id); return existing; });
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
            catch (Exception ex)
            {
                if (state.Options.AckMode == MessageAckMode.ManualAck)
                {
                    _pendingAck.TryRemove(message.Guid, out _);
                }
                // Entry stays in the Pending Entries List; the reclaim pass will redeliver it.
                await InvokeHandlerErrorAsync(ex, message).ConfigureAwait(false);
            }
        }

        private async Task InvokeHandlerErrorAsync(Exception ex, QueueMessage message)
        {
            var hook = OnHandlerError;
            if (hook == null)
            {
                return;
            }

            try
            {
                await hook(ex, message).ConfigureAwait(false);
            }
            catch
            {
                // The error hook must never break the poll loop.
            }
        }

        /// <summary>
        /// The id of the newest entry in the stream, or <c>"0-0"</c> when the stream is empty or does not exist
        /// yet — the concrete position from which a non-group subscription reads only messages added after it
        /// started.
        /// </summary>
        private static async Task<string> ResolveStreamTailAsync(IDatabase db, string streamKey)
        {
            var newest = await db.StreamRangeAsync(streamKey, "-", "+", count: 1, messageOrder: Order.Descending).ConfigureAwait(false);
            return newest != null && newest.Length > 0 ? newest[0].Id.ToString() : "0-0";
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
                queueMessage.Guid = messageId;
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

        private static async Task EnsureConsumerGroupAsync(IDatabase db, string streamKey, string groupName, bool fromBeginning)
        {
            try
            {
                // SH-M102 (decided by the user 2026-10-18): a new group starts where FromBeginning says — at the start of
                // the stream when true, at its tail ("$", new messages only) when false, as ConsumerOptions documents.
                // It always started at 0-0, so attaching a new group replayed the whole stream. An existing group keeps
                // its position. MKSTREAM creates the stream if it doesn't exist.
                await db.StreamCreateConsumerGroupAsync(streamKey, groupName, fromBeginning ? "0-0" : "$", createStream: true).ConfigureAwait(false);
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
            public List<RedisValue> StreamEntryIds { get; } = new();

            public void Add(RedisValue entryId)
            {
                lock (StreamEntryIds)
                {
                    if (!StreamEntryIds.Contains(entryId)) StreamEntryIds.Add(entryId);
                }
            }

            public RedisValue[] EntryIds()
            {
                lock (StreamEntryIds) return StreamEntryIds.ToArray();
            }
        }
    }
}
