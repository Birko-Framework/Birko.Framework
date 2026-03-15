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

namespace Birko.EventBus.MessageQueue
{
    /// <summary>
    /// Distributed event bus backed by <see cref="IMessageQueue"/>.
    /// Publishes events as serialized <see cref="EventEnvelope"/> messages to queue topics.
    /// Subscribes to topics and deserializes envelopes back into strongly-typed events for handler dispatch.
    /// </summary>
    public class DistributedEventBus : IEventBus
    {
        private readonly IMessageQueue _messageQueue;
        private readonly DistributedEventBusOptions _options;
        private readonly IMessageSerializer _serializer;
        private readonly EventPipeline _pipeline;
        private readonly IReadOnlyList<IEventEnricher> _enrichers;
        private readonly IServiceProvider? _serviceProvider;
        private readonly ConcurrentDictionary<Type, List<object>> _subscriptions = new();
        private readonly ConcurrentBag<ISubscription> _queueSubscriptions = new();
        private bool _disposed;

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
            _serializer = _options.Serializer ?? new JsonMessageSerializer();
            _pipeline = new EventPipeline(behaviors ?? []);
            _enrichers = enrichers?.ToList() ?? [];
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var context = EventContext.From(@event);

            // Run enrichers
            foreach (var enricher in _enrichers)
            {
                await enricher.EnrichAsync(@event, context, cancellationToken).ConfigureAwait(false);
            }

            // Build envelope
            var envelope = new EventEnvelope
            {
                EventId = @event.EventId,
                EventType = @event.GetType().AssemblyQualifiedName!,
                Source = @event.Source,
                OccurredAt = @event.OccurredAt,
                CorrelationId = context.CorrelationId,
                TenantId = context.TenantId,
                Payload = _serializer.Serialize(@event),
                Headers = new(context.Metadata)
            };

            // Serialize envelope and send to topic (use type-based convention for consistent routing)
            var topic = _options.TopicConvention.GetTopic(@event.GetType());
            var body = _serializer.Serialize(envelope);

            var headers = new MessageHeaders
            {
                CorrelationId = context.CorrelationId?.ToString(),
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
        public IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

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
            ObjectDisposedException.ThrowIf(_disposed, this);

            var topic = _options.TopicConvention.GetTopic(typeof(TEvent));

            var sub = await _messageQueue.Consumer.SubscribeAsync(topic, async (message, ct) =>
            {
                var envelope = _serializer.Deserialize<EventEnvelope>(message.Body);
                if (envelope == null)
                {
                    return;
                }

                var eventType = Type.GetType(envelope.EventType);
                if (eventType == null || !typeof(TEvent).IsAssignableFrom(eventType))
                {
                    return;
                }

                var @event = (TEvent?)_serializer.Deserialize(envelope.Payload, eventType);
                if (@event == null)
                {
                    return;
                }

                var context = new EventContext
                {
                    EventId = envelope.EventId,
                    Source = envelope.Source,
                    CorrelationId = envelope.CorrelationId,
                    TenantId = envelope.TenantId,
                    DeliveryCount = message.Headers?.Custom.ContainsKey("x-delivery-count") == true
                        ? int.TryParse(message.Headers.Custom["x-delivery-count"], out var dc) ? dc : 1
                        : 1,
                    Metadata = envelope.Headers
                };

                var handlers = GetHandlers<TEvent>();
                if (handlers.Count == 0)
                {
                    return;
                }

                await _pipeline.ExecuteAsync(@event, context, async () =>
                {
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            await handler.HandleAsync(@event, context, ct).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Error isolation: continue to next handler
                        }
                    }
                }, ct).ConfigureAwait(false);
            }, _options.ConsumerOptions, cancellationToken).ConfigureAwait(false);

            _queueSubscriptions.Add(sub);
        }

        private List<IEventHandler<TEvent>> GetHandlers<TEvent>() where TEvent : IEvent
        {
            var handlers = new List<IEventHandler<TEvent>>();

            // DI-registered handlers
            if (_serviceProvider != null)
            {
                var serviceType = typeof(IEnumerable<IEventHandler<TEvent>>);
                if (_serviceProvider.GetService(serviceType) is IEnumerable<IEventHandler<TEvent>> diHandlers)
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var sub in _queueSubscriptions)
            {
                sub.UnsubscribeAsync().GetAwaiter().GetResult();
            }

            _subscriptions.Clear();
        }

        private sealed class DistributedEventSubscription : IEventSubscription
        {
            private readonly Action _unsubscribe;
            private bool _isActive = true;

            public Type EventType { get; }
            public bool IsActive => _isActive;

            public DistributedEventSubscription(Type eventType, Action unsubscribe)
            {
                EventType = eventType;
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                if (!_isActive)
                {
                    return;
                }

                _isActive = false;
                _unsubscribe();
            }
        }
    }
}
