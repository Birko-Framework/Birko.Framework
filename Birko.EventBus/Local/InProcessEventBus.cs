using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Enrichment;
using Birko.EventBus.Pipeline;
using Microsoft.Extensions.DependencyInjection;

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

            // TASK-565 (SH-M120): DI handlers come from a scope of this publish's own, never from the provider the
            // bus was built with. AddEventBus builds the bus as a singleton over the ROOT provider, and the outbox
            // dispatches from a background loop with no request scope, so resolving from _serviceProvider either
            // throws under ValidateScopes or hands every dispatch one root-owned, never-disposed instance of each
            // scoped dependency. One scope per publish: every handler of this event shares it (including parallel
            // ones under MaxConcurrency > 1), and it ends with the dispatch.
            await using var scope = _serviceProvider?.GetService(typeof(IServiceScopeFactory)) is IServiceScopeFactory scopeFactory
                ? scopeFactory.CreateAsyncScope()
                : (AsyncServiceScope?)null;

            // Collect handlers: DI-registered + manual subscriptions
            var handlers = GetHandlers<TEvent>(scope?.ServiceProvider ?? _serviceProvider);

            // SH-M119: the pipeline runs even when no handler is registered, so its behaviours see every publish:
            // deduplication records the EventId (a later handler then does not process it as first-seen) and auditing
            // or metrics behaviours leave a trace. It returned before the pipeline.
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

        private List<IEventHandler<TEvent>> GetHandlers<TEvent>(IServiceProvider? serviceProvider) where TEvent : IEvent
        {
            var handlers = new List<IEventHandler<TEvent>>();

            // DI-registered handlers
            if (serviceProvider != null)
            {
                var serviceType = typeof(IEnumerable<IEventHandler<TEvent>>);
                if (serviceProvider.GetService(serviceType) is IEnumerable<IEventHandler<TEvent>> diHandlers)
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
                catch (Exception ex) when (_options.ErrorHandling == ErrorHandlingMode.Continue)
                {
                    // Error isolation: report (CR-M185) then continue to next handler.
                    _options.OnHandlerError?.Invoke(@event, ex);
                }
                catch (Exception ex)
                {
                    // Stop mode: report (CR-M185) then propagate to abort the loop.
                    _options.OnHandlerError?.Invoke(@event, ex);
                    throw;
                }
            }
        }

        private async Task DispatchParallelAsync<TEvent>(List<IEventHandler<TEvent>> handlers, TEvent @event, EventContext context, CancellationToken cancellationToken) where TEvent : IEvent
        {
            using var semaphore = new SemaphoreSlim(_options.MaxConcurrency);
            // CR-M184: in Stop mode a handler failure must halt the remaining handlers. All tasks are
            // launched eagerly (bounded by the semaphore), so we cancel this linked source on the first
            // failure — queued handlers waiting on the semaphore and any that observe the token abort
            // instead of running to completion (the old code let every handler finish regardless of mode).
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = cts.Token;
            Exception? firstStopError = null;

            var tasks = handlers.Select(async handler =>
            {
                try
                {
                    await semaphore.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await handler.HandleAsync(@event, context, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Aborted by our Stop-mode cancellation (not the caller's token) — the real cause is
                    // captured in firstStopError and rethrown after WhenAll; swallow this follow-on OCE.
                }
                catch (Exception ex) when (_options.ErrorHandling == ErrorHandlingMode.Continue)
                {
                    // Error isolation: report (CR-M185) then let the other handlers proceed.
                    _options.OnHandlerError?.Invoke(@event, ex);
                }
                catch (Exception ex)
                {
                    // Stop mode: report (CR-M185), capture the first cause, and cancel the rest.
                    _options.OnHandlerError?.Invoke(@event, ex);
                    Interlocked.CompareExchange(ref firstStopError, ex, null);
                    cts.Cancel();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Surface the original handler failure (not a follow-on cancellation) in Stop mode.
            if (firstStopError != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstStopError).Throw();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _subscriptions.Clear();
        }

        private sealed class InProcessEventSubscription : IEventSubscription
        {
            private readonly Action _unsubscribe;
            // CR-L250: int (1 = active, 0 = disposed) so the dispose guard can be an atomic
            // Interlocked check-and-clear; a plain bool check-then-set let two concurrent Dispose
            // calls both pass the guard and invoke _unsubscribe twice.
            private int _isActive = 1;

            public Type EventType { get; }
            public bool IsActive => Volatile.Read(ref _isActive) == 1;

            public InProcessEventSubscription(Type eventType, Action unsubscribe)
            {
                EventType = eventType;
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                // Only the caller that flips 1 → 0 runs _unsubscribe; a concurrent (or repeat) Dispose
                // sees the prior value 0 and returns.
                if (Interlocked.Exchange(ref _isActive, 0) == 0)
                {
                    return;
                }

                _unsubscribe();
            }
        }
    }
}
