using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;
using Birko.Time;

namespace Birko.EventBus.Outbox.Publishing
{
    /// <summary>
    /// Background processor that polls the outbox store for pending entries
    /// and publishes them via the inner event bus.
    /// </summary>
    public class OutboxProcessor
    {
        private readonly IOutboxStore _store;
        private readonly IEventBus _publisher;
        private readonly IMessageSerializer _serializer;
        private readonly OutboxOptions _options;
        private readonly IDateTimeProvider _clock;
        private readonly IEventScopeAccessor _scopeAccessor;

        // CR-L259: last time the retention cleanup actually ran, so CleanupIfDueAsync can throttle it to
        // OutboxOptions.CleanupInterval instead of every poll. Only touched from the single background loop
        // (OutboxProcessorHostedService), so it needs no synchronization.
        private DateTime _lastCleanupUtc = DateTime.MinValue;

        /// <summary>
        /// Creates a new outbox processor.
        /// </summary>
        /// <param name="store">The outbox persistence store.</param>
        /// <param name="publisher">The event bus to publish events through (the inner bus, not the OutboxEventBus).</param>
        /// <param name="options">Processor options.</param>
        /// <param name="serializer">Serializer for deserializing event payloads. Defaults to JsonMessageSerializer.</param>
        /// <param name="clock">Clock used for cleanup scheduling. Defaults to <see cref="SystemDateTimeProvider"/>.</param>
        /// <param name="scopeAccessor">
        /// Restores the ambient scope (tenant, correlation) each entry was published under before re-publishing
        /// it, from the persisted <see cref="OutboxEntry.TenantGuid"/> (STORY-046). The processor runs OUTSIDE
        /// the publishing request's async flow, so without this the inner bus re-enriches from an unset ambient
        /// and handlers see no tenant — which throws under <c>TenantIsolationMode.Strict</c>. Defaults to
        /// <see cref="NullEventScopeAccessor"/> (no-op — unchanged behaviour until a bridge is registered).
        /// </param>
        public OutboxProcessor(
            IOutboxStore store,
            IEventBus publisher,
            OutboxOptions? options = null,
            IMessageSerializer? serializer = null,
            IDateTimeProvider? clock = null,
            IEventScopeAccessor? scopeAccessor = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _options = options ?? new OutboxOptions();
            _serializer = serializer ?? new JsonMessageSerializer();
            _clock = clock ?? new SystemDateTimeProvider();
            _scopeAccessor = scopeAccessor ?? NullEventScopeAccessor.Instance;
        }

        /// <summary>
        /// Processes one batch of pending outbox entries.
        /// Returns the number of entries processed.
        /// </summary>
        public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
        {
            var entries = await _store.GetPendingAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);
            if (entries.Count == 0)
            {
                return 0;
            }

            var processed = 0;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var eventType = Type.GetType(entry.EventType);
                    if (eventType == null)
                    {
                        await _store.MarkFailedAsync(entry.Id, $"Cannot resolve type: {entry.EventType}", _options.MaxAttempts, cancellationToken).ConfigureAwait(false);
                        processed++;
                        continue;
                    }

                    var @event = _serializer.Deserialize(entry.Payload, eventType) as IEvent;
                    if (@event == null)
                    {
                        await _store.MarkFailedAsync(entry.Id, $"Cannot deserialize payload for type: {entry.EventType}", _options.MaxAttempts, cancellationToken).ConfigureAwait(false);
                        processed++;
                        continue;
                    }

                    // STORY-046: re-establish the ambient scope this entry was published under (from the
                    // persisted TenantGuid) before re-publishing. The processor runs outside the original
                    // request's async flow, so otherwise the inner bus re-enriches from an unset ambient and
                    // handlers observe no tenant — which throws under TenantIsolationMode.Strict. No-op unless
                    // a scope bridge is registered.
                    var scopeContext = EventContext.From(@event, entry.TenantGuid, metadata: entry.Headers);
                    scopeContext.CorrelationId = entry.CorrelationId;

                    // Publish via the inner bus (which sends to MessageQueue or dispatches in-process)
                    await _scopeAccessor
                        .RunWithScopeAsync(scopeContext, () => PublishEventAsync(@event, cancellationToken), cancellationToken)
                        .ConfigureAwait(false);
                    await _store.MarkPublishedAsync(entry.Id, cancellationToken).ConfigureAwait(false);
                    processed++;
                }
                catch (Exception ex)
                {
                    // CR-M189: record the underlying cause, not the opaque reflection wrapper
                    // ("Exception has been thrown by the target of an invocation.").
                    var cause = Unwrap(ex);
                    await _store.MarkFailedAsync(entry.Id, cause.Message, _options.MaxAttempts, cancellationToken).ConfigureAwait(false);
                    processed++;
                }
            }

            return processed;
        }

        /// <summary>
        /// Cleans up old published/failed entries based on retention period. Runs unconditionally — the
        /// background loop uses <see cref="CleanupIfDueAsync"/> to throttle it (CR-L259).
        /// </summary>
        public Task CleanupAsync(CancellationToken cancellationToken = default)
        {
            var cutoff = _clock.UtcNow - _options.RetentionPeriod;
            return _store.CleanupAsync(cutoff, cancellationToken);
        }

        /// <summary>
        /// Runs <see cref="CleanupAsync"/> only if at least <see cref="OutboxOptions.CleanupInterval"/> has
        /// elapsed since the last cleanup (CR-L259) — so the background loop can call it on every poll
        /// without triggering a full retention scan/delete each time. The first call always runs. Returns
        /// <c>true</c> when cleanup was performed.
        /// </summary>
        public async Task<bool> CleanupIfDueAsync(CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;
            if (now - _lastCleanupUtc < _options.CleanupInterval)
            {
                return false;
            }

            await CleanupAsync(cancellationToken).ConfigureAwait(false);
            _lastCleanupUtc = now;
            return true;
        }

        private Task PublishEventAsync(IEvent @event, CancellationToken cancellationToken)
        {
            // Use reflection to call PublishAsync<TEvent> with the correct type
            var method = typeof(IEventBus)
                .GetMethod(nameof(IEventBus.PublishAsync))!
                .MakeGenericMethod(@event.GetType());

            try
            {
                return (Task)method.Invoke(_publisher, [@event, cancellationToken])!;
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                // CR-M189: a synchronous throw from PublishAsync (e.g. ObjectDisposedException before the
                // first await) is wrapped by MethodInfo.Invoke — surface the real cause as a faulted task
                // so the ProcessBatch catch records the underlying message, not the wrapper.
                return Task.FromException(tie.InnerException);
            }
        }

        /// <summary>CR-M189: unwrap the reflection wrapper so LastError carries the underlying cause.</summary>
        private static Exception Unwrap(Exception ex)
            => ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex;
    }
}
