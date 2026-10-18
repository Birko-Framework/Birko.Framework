using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Enrichment;
using Birko.EventBus.Pipeline;
using Birko.MessageQueue;
using Birko.MessageQueue.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.EventBus.MessageQueue
{
    /// <summary>
    /// Distributed event bus backed by <see cref="IMessageQueue"/>.
    /// Publishes events as serialized <see cref="EventEnvelope"/> messages to queue topics.
    /// Subscribes to topics and deserializes envelopes back into strongly-typed events for handler dispatch.
    /// </summary>
    public class DistributedEventBus : IEventBus, IAsyncDisposable
    {
        private readonly IMessageQueue _messageQueue;
        private readonly DistributedEventBusOptions _options;
        private readonly IMessageSerializer _serializer;
        private readonly EventPipeline _pipeline;
        private readonly IReadOnlyList<IEventEnricher> _enrichers;
        private readonly IServiceProvider? _serviceProvider;

        /// <summary>
        /// Raised when a delivered message can never be dispatched — its envelope or payload does not decode, or it
        /// names an event type that is not this subscription's — with the reason. The message is then acknowledged
        /// and dropped (SH-M116: it was dropped with no trace). A throwing subscriber is ignored.
        /// </summary>
        /// <remarks>
        /// An event type this process cannot load is NOT dropped: the callback throws, so the transport redelivers it
        /// and a consumer that knows the type (after a rolling deploy) can take it (decided by the user 2026-10-18).
        /// </remarks>
        public event Action<QueueMessage, string>? MessageDropped;
        private readonly ConcurrentDictionary<Type, List<object>> _subscriptions = new();
        private readonly ConcurrentBag<ISubscription> _queueSubscriptions = new();
        private int _disposed; // SH-L116: Dispose and DisposeAsync race to one teardown
        private readonly global::Birko.RetryPolicy? _retry;
        private readonly RedeliveryMode _redelivery;

        /// <summary>The header naming the topic a dead-lettered message came from.</summary>
        public const string OriginalDestinationHeader = "x-original-destination";

        /// <summary>The header carrying why a message was dead-lettered.</summary>
        public const string DeadLetterReasonHeader = "x-dead-letter-reason";

        /// <summary>
        /// Creates a new distributed event bus.
        /// </summary>
        /// <param name="messageQueue">The underlying message queue transport.</param>
        /// <param name="options">Distributed event bus options.</param>
        /// <param name="serviceProvider">DI container for resolving handlers.</param>
        /// <param name="behaviors">Pipeline behaviors.</param>
        /// <param name="enrichers">Event enrichers.</param>
        public DistributedEventBus(
            IMessageQueue messageQueue,
            DistributedEventBusOptions? options = null,
            IServiceProvider? serviceProvider = null,
            IEnumerable<IEventPipelineBehavior>? behaviors = null,
            IEnumerable<IEventEnricher>? enrichers = null)
        {
            _messageQueue = messageQueue ?? throw new ArgumentNullException(nameof(messageQueue));
            _options = options ?? new DistributedEventBusOptions();
            // TASK-668 (lifting SH-L114's refusal): a retry policy or dead-letter options are applied by the bus, on a
            // transport that can tell it how a failed delivery comes back. One that cannot count deliveries is refused,
            // since the rule would never fire.
            if (_options.RetryPolicy != null || _options.DeadLetterOptions != null)
            {
                _redelivery = (messageQueue as IRedeliveryCapability)?.GetRedeliveryMode(_options.ConsumerOptions) ?? RedeliveryMode.None;
                if (_redelivery == RedeliveryMode.None)
                {
                    throw new NotSupportedException(
                        $"{messageQueue.GetType().Name} cannot count deliveries of a failed message, so RetryPolicy and DeadLetterOptions would never apply; leave them null. Redis needs a consumer group with PendingRetryMilliseconds above 0.");
                }
                if (_options.RetryPolicy is { MaxRetries: < 0 })
                {
                    throw new ArgumentOutOfRangeException(nameof(options), _options.RetryPolicy.MaxRetries, "RetryPolicy.MaxRetries must not be negative.");
                }
                _retry = _options.RetryPolicy ?? global::Birko.RetryPolicy.Default;
            }
            _serializer = _options.Serializer ?? new JsonMessageSerializer();
            _pipeline = new EventPipeline(behaviors ?? []);
            _enrichers = enrichers?.ToList() ?? [];
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var context = EventContext.From(@event);

            // Run enrichers
            foreach (var enricher in _enrichers)
            {
                await enricher.EnrichAsync(@event, context, cancellationToken).ConfigureAwait(false);
            }

            // Build envelope
            var envelope = new EventEnvelope
            {
                EventGuid = @event.EventGuid,
                EventType = @event.GetType().AssemblyQualifiedName!,
                Source = @event.Source,
                OccurredAt = @event.OccurredAt,
                CorrelationGuid = context.CorrelationGuid,
                TenantGuid = context.TenantGuid,
                Payload = _serializer.Serialize(@event),
                Headers = new(context.Metadata)
            };

            // Serialize envelope and send to topic (use type-based convention for consistent routing)
            var topic = _options.TopicConvention.GetTopic(@event.GetType());
            var body = _serializer.Serialize(envelope);

            var headers = new MessageHeaders
            {
                CorrelationId = context.CorrelationGuid?.ToString(),
                ContentType = _serializer.ContentType
            };

            var message = new QueueMessage
            {
                Body = body,
                PayloadType = typeof(EventEnvelope).AssemblyQualifiedName,
                Headers = headers
            };

            await _messageQueue.Producer.SendAsync(topic, message, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        /// <remarks>
        /// CR-L256: registering a handler here is <b>necessary but not sufficient</b> to receive events over
        /// the distributed bus. Manually-subscribed handlers are only ever invoked from inside the
        /// <see cref="SubscribeToTransportAsync{TEvent}"/> delivery callback (via <c>GetHandlers</c>), so
        /// events of <typeparamref name="TEvent"/> only reach this handler once a matching transport
        /// subscription exists — call <see cref="SubscribeToTransportAsync{TEvent}"/> for that type (or let
        /// <see cref="AutoSubscriber"/>/<see cref="DistributedEventBusHostedService"/> create it from DI on
        /// startup). Calling <c>Subscribe</c> alone silently receives nothing. Auto-wiring the transport here
        /// is intentionally not done: <c>Subscribe</c> is synchronous and the transport subscribe is
        /// network-bound async, and CR-M188 removed sync-over-async from this class to avoid deadlocks.
        /// </remarks>
        public IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var eventType = typeof(TEvent);
            var handlers = _subscriptions.GetOrAdd(eventType, _ => []);
            lock (handlers)
            {
                handlers.Add(handler);
            }

            return new DistributedEventSubscription(eventType, () =>
            {
                lock (handlers)
                {
                    handlers.Remove(handler);
                }
            });
        }

        /// <summary>
        /// Subscribes to a message queue topic for the given event type.
        /// Deserializes incoming envelopes and dispatches to all registered handlers.
        /// Call this for each event type you want to receive from the distributed bus.
        /// </summary>
        /// <typeparam name="TEvent">The event type to subscribe to.</typeparam>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task SubscribeToTransportAsync<TEvent>(CancellationToken cancellationToken = default) where TEvent : class, IEvent
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var topic = _options.TopicConvention.GetTopic(typeof(TEvent));

            var sub = await _messageQueue.Consumer.SubscribeAsync(topic, async (message, ct) =>
            {
                try
                {
                    await DispatchAsync<TEvent>(message, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (_retry != null && !(ex is OperationCanceledException && ct.IsCancellationRequested))
                {
                    if (!await HandleFailedDeliveryAsync(topic, message, ex, ct).ConfigureAwait(false))
                    {
                        throw;
                    }
                }
            }, _options.ConsumerOptions, cancellationToken).ConfigureAwait(false);

            _queueSubscriptions.Add(sub);
        }

        /// <summary>
        /// TASK-668: applies the retry policy to a delivery whose dispatch failed. Returns true when the failure is dealt
        /// with here (re-sent, dead-lettered or dropped), so the delivery completes; false to let it fault, so the
        /// transport redelivers it.
        /// </summary>
        private async Task<bool> HandleFailedDeliveryAsync(string topic, QueueMessage message, Exception failure, CancellationToken ct)
        {
            var delivery = message.Headers?.Custom.TryGetValue(DeliveryCountHeader, out var seen) == true
                && int.TryParse(seen, out var n) && n > 0 ? n : 1;

            if (delivery <= _retry!.MaxRetries)
            {
                if (_redelivery == RedeliveryMode.Redelivered)
                {
                    return false; // the entry stays pending and the transport redelivers it with the next count
                }

                var retry = message.Copy();
                retry.Headers.Custom[DeliveryCountHeader] = (delivery + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                retry.Delay = _retry.GetDelay(delivery);
                await _messageQueue.Producer.SendAsync(topic, retry, ct).ConfigureAwait(false);
                return true;
            }

            var deadLetter = _options.DeadLetterOptions;
            if (deadLetter is not { Enabled: true })
            {
                ReportDropped(message, $"Delivery {delivery} failed and the retries are spent ({_retry.MaxRetries}): {failure.Message}");
                return true;
            }

            var dead = message.Copy();
            dead.Delay = null;
            dead.Headers.Custom[OriginalDestinationHeader] = topic;
            var reason = $"{failure.GetType().Name}: {failure.Message}";
            dead.Headers.Custom[DeadLetterReasonHeader] = reason.Length > 1000 ? reason[..1000] : reason;
            // If this send fails, the delivery faults and comes back: the message is not lost between the two queues.
            await _messageQueue.Producer.SendAsync(deadLetter.GetDeadLetterDestination(topic), dead, ct).ConfigureAwait(false);
            return true;
        }

        private const string DeliveryCountHeader = "x-delivery-count";

        private async Task DispatchAsync<TEvent>(QueueMessage message, CancellationToken ct) where TEvent : class, IEvent
        {
            var envelope = _serializer.Deserialize<EventEnvelope>(message.Body);
            if (envelope == null)
            {
                ReportDropped(message, "The message body is not an event envelope.");
                return;
            }

            var eventType = Type.GetType(envelope.EventType);
            if (eventType == null)
            {
                // Not loadable here, e.g. a newer publisher during a rolling deploy: redelivered, not lost.
                throw new InvalidOperationException(
                    $"Event type '{envelope.EventType}' cannot be loaded by this consumer; the message is left for redelivery.");
            }
            if (!typeof(TEvent).IsAssignableFrom(eventType))
            {
                ReportDropped(message, $"Event type '{envelope.EventType}' is not a {typeof(TEvent).Name}, which this subscription handles.");
                return;
            }

            var @event = (TEvent?)_serializer.Deserialize(envelope.Payload, eventType);
            if (@event == null)
            {
                ReportDropped(message, $"The payload of event {envelope.EventGuid} does not decode to {eventType.Name}.");
                return;
            }

            var context = new EventContext
            {
                EventGuid = envelope.EventGuid,
                Source = envelope.Source,
                CorrelationGuid = envelope.CorrelationGuid,
                TenantGuid = envelope.TenantGuid,
                DeliveryCount = message.Headers?.Custom.ContainsKey("x-delivery-count") == true
                    ? int.TryParse(message.Headers.Custom["x-delivery-count"], out var dc) ? dc : 1
                    : 1,
                Metadata = envelope.Headers
            };

            // TASK-566: DI handlers come from a scope of this message's own, disposed once dispatch is over: a message
            // arrives on a transport callback with no request scope, so the provider the singleton bus was built with
            // is the root, where a scoped dependency throws under ValidateScopes and is otherwise one root-owned,
            // never-disposed instance shared by every message. The same contract as InProcessEventBus (TASK-565).
            await using var scope = _serviceProvider?.GetService(typeof(IServiceScopeFactory)) is IServiceScopeFactory scopeFactory
                ? scopeFactory.CreateAsyncScope()
                : (AsyncServiceScope?)null;

            var handlers = GetHandlers<TEvent>(scope?.ServiceProvider ?? _serviceProvider);

            // SH-M117: handlers run in the ambient scope the event was published under (the tenant, when a bridge
            // is registered), as the outbox processor's re-publish does. The scope accessor was documented for this
            // callback but never applied, so a handler relying on the ambient tenant failed under Strict isolation.
            var scopeAccessor = (scope?.ServiceProvider ?? _serviceProvider)?.GetService(typeof(IEventScopeAccessor)) as IEventScopeAccessor
                ?? NullEventScopeAccessor.Instance;

            // SH-M119: the pipeline runs even with no handlers, so its behaviours (deduplication, auditing) see the
            // event, as InProcessEventBus does.
            await scopeAccessor.RunWithScopeAsync(context, () => _pipeline.ExecuteAsync(@event, context, async () =>
            {
                List<Exception>? failures = null;
                foreach (var handler in handlers)
                {
                    try
                    {
                        await handler.HandleAsync(@event, context, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Error isolation: keep dispatching to the remaining handlers, but record
                        // the failure and rethrow it after the loop. The transport drives
                        // retry / dead-letter by whether this delivery callback faults, so
                        // swallowing every exception acked failed messages and lost the event
                        // (CR-H114).
                        (failures ??= new List<Exception>()).Add(ex);
                    }
                }

                if (failures != null)
                {
                    throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
                }
            }, ct), ct).ConfigureAwait(false);
        }

        private void ReportDropped(QueueMessage message, string reason)
        {
            try
            {
                MessageDropped?.Invoke(message, reason);
            }
            catch
            {
                // A diagnostic channel must survive its own subscriber (rule 25).
            }
        }

        private List<IEventHandler<TEvent>> GetHandlers<TEvent>(IServiceProvider? provider) where TEvent : IEvent
        {
            var handlers = new List<IEventHandler<TEvent>>();

            // DI-registered handlers
            if (provider != null)
            {
                var serviceType = typeof(IEnumerable<IEventHandler<TEvent>>);
                if (provider.GetService(serviceType) is IEnumerable<IEventHandler<TEvent>> diHandlers)
                {
                    handlers.AddRange(diHandlers);
                }
            }

            // Manual subscriptions
            if (_subscriptions.TryGetValue(typeof(TEvent), out var manualHandlers))
            {
                lock (manualHandlers)
                {
                    foreach (var handler in manualHandlers)
                    {
                        if (handler is IEventHandler<TEvent> typedHandler)
                        {
                            handlers.Add(typedHandler);
                        }
                    }
                }
            }

            return handlers;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // CR-M188: dispose synchronously via ISubscription.Dispose() (ISubscription : IDisposable)
            // instead of blocking on the async UnsubscribeAsync — the old .GetAwaiter().GetResult()
            // was sync-over-async on a potentially network-bound unsubscribe and could deadlock under a
            // synchronization context. Prefer DisposeAsync for a graceful async unsubscribe.
            foreach (var sub in _queueSubscriptions)
            {
                sub.Dispose();
            }

            _subscriptions.Clear();
        }

        /// <summary>
        /// CR-M188: async disposal that awaits each queue subscription's UnsubscribeAsync — use this
        /// (e.g. <c>await using</c>) rather than the synchronous <see cref="Dispose"/> when an ordered,
        /// non-blocking unsubscribe is required.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            foreach (var sub in _queueSubscriptions)
            {
                await sub.UnsubscribeAsync().ConfigureAwait(false);
            }

            _subscriptions.Clear();
        }

        private sealed class DistributedEventSubscription : IEventSubscription
        {
            private readonly Action _unsubscribe;
            private int _released;

            public Type EventType { get; }
            public bool IsActive => Volatile.Read(ref _released) == 0;

            public DistributedEventSubscription(Type eventType, Action unsubscribe)
            {
                EventType = eventType;
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                // SH-L115: one unsubscribe, however many callers race to it (as InProcessEventSubscription since CR-L250).
                if (Interlocked.Exchange(ref _released, 1) == 0)
                {
                    _unsubscribe();
                }
            }
        }
    }
}
