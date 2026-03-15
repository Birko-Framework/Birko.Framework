using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Enrichment;
using Birko.EventBus.Pipeline;

namespace Birko.EventBus.Local
{
    /// <summary>
    /// In-process event bus for modular monolith scenarios.
    /// Resolves handlers from DI (IServiceProvider) and manual subscriptions.
    /// Dispatches events through the pipeline, then to all matching handlers.
    /// </summary>
    public class InProcessEventBus : IEventBus
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly InProcessEventBusOptions _options;
        private readonly EventPipeline _pipeline;
        private readonly IReadOnlyList<IEventEnricher> _enrichers;
        private readonly ConcurrentDictionary<Type, List<object>> _subscriptions = new();
        private bool _disposed;

        /// <summary>
        /// Creates a new in-process event bus.
        /// </summary>
        /// <param name="serviceProvider">DI container for resolving IEventHandler&lt;T&gt; instances. Can be null for manual-only subscriptions.</param>
        /// <param name="options">Bus options.</param>
        /// <param name="behaviors">Pipeline behaviors (executed in order).</param>
        /// <param name="enrichers">Event enrichers (executed in order before dispatch).</param>
        public InProcessEventBus(
            IServiceProvider? serviceProvider = null,
            InProcessEventBusOptions? options = null,
            IEnumerable<IEventPipelineBehavior>? behaviors = null,
            IEnumerable<IEventEnricher>? enrichers = null)
        {
            _serviceProvider = serviceProvider;
            _options = options ?? new InProcessEventBusOptions();
            _pipeline = new EventPipeline(behaviors ?? []);
            _enrichers = enrichers?.ToList() ?? [];
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

            // Collect handlers: DI-registered + manual subscriptions
            var handlers = GetHandlers<TEvent>();

            if (handlers.Count == 0)
            {
                return;
            }

            // Dispatch through pipeline
            await _pipeline.ExecuteAsync(@event, context, async () =>
            {
                if (_options.MaxConcurrency <= 1)
                {
                    await DispatchSequentialAsync(handlers, @event, context, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DispatchParallelAsync(handlers, @event, context, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
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

            return new InProcessEventSubscription(eventType, () =>
            {
                lock (handlers)
                {
                    handlers.Remove(handler);
                }
            });
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

        private async Task DispatchSequentialAsync<TEvent>(List<IEventHandler<TEvent>> handlers, TEvent @event, EventContext context, CancellationToken cancellationToken) where TEvent : IEvent
        {
            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(@event, context, cancellationToken).ConfigureAwait(false);
                }
                catch when (_options.ErrorHandling == ErrorHandlingMode.Continue)
                {
                    // Error isolation: continue to next handler
                }
            }
        }

        private async Task DispatchParallelAsync<TEvent>(List<IEventHandler<TEvent>> handlers, TEvent @event, EventContext context, CancellationToken cancellationToken) where TEvent : IEvent
        {
            using var semaphore = new SemaphoreSlim(_options.MaxConcurrency);
            var tasks = handlers.Select(async handler =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await handler.HandleAsync(@event, context, cancellationToken).ConfigureAwait(false);
                }
                catch when (_options.ErrorHandling == ErrorHandlingMode.Continue)
                {
                    // Error isolation
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _disposed = true;
            _subscriptions.Clear();
        }

        private sealed class InProcessEventSubscription : IEventSubscription
        {
            private readonly Action _unsubscribe;
            private bool _isActive = true;

            public Type EventType { get; }
            public bool IsActive => _isActive;

            public InProcessEventSubscription(Type eventType, Action unsubscribe)
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
