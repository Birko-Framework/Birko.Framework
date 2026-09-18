using System;
using Birko.EventBus.Deduplication;
using Birko.EventBus.Enrichment;
using Birko.EventBus.Local;
using Birko.EventBus.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.EventBus.Extensions
{
    /// <summary>
    /// DI registration extensions for Birko.EventBus.
    /// </summary>
    public static class EventBusServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the in-process event bus as singleton IEventBus.
        /// Scans the service collection for IEventHandler&lt;T&gt; registrations.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional options configuration.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEventBus(this IServiceCollection services, Action<InProcessEventBusOptions>? configure = null)
        {
            var options = new InProcessEventBusOptions();
            configure?.Invoke(options);

            services.AddSingleton(options);
            services.AddSingleton<IEventBus>(sp =>
            {
                var behaviors = sp.GetServices<IEventPipelineBehavior>();
                var enrichers = sp.GetServices<IEventEnricher>();
                return new InProcessEventBus(sp, options, behaviors, enrichers);
            });

            return services;
        }

        /// <summary>
        /// Registers a custom IEventBus implementation.
        /// </summary>
        /// <typeparam name="TBus">The event bus implementation type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEventBus<TBus>(this IServiceCollection services) where TBus : class, IEventBus
        {
            services.AddSingleton<IEventBus, TBus>();
            return services;
        }

        /// <summary>
        /// Registers an event handler for a specific event type.
        /// </summary>
        /// <typeparam name="TEvent">The event type.</typeparam>
        /// <typeparam name="THandler">The handler type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
            where TEvent : IEvent
            where THandler : class, IEventHandler<TEvent>
        {
            services.AddTransient<IEventHandler<TEvent>, THandler>();
            return services;
        }

        /// <summary>
        /// Registers a pipeline behavior.
        /// Behaviors execute in registration order (first registered = outermost).
        /// </summary>
        /// <typeparam name="TBehavior">The behavior type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEventPipelineBehavior<TBehavior>(this IServiceCollection services)
            where TBehavior : class, IEventPipelineBehavior
        {
            services.AddSingleton<IEventPipelineBehavior, TBehavior>();
            return services;
        }

        /// <summary>
        /// Registers an event enricher.
        /// Enrichers run in registration order before dispatch.
        /// </summary>
        /// <typeparam name="TEnricher">The enricher type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEventEnricher<TEnricher>(this IServiceCollection services)
            where TEnricher : class, IEventEnricher
        {
            services.AddSingleton<IEventEnricher, TEnricher>();
            return services;
        }

        /// <summary>
        /// Registers the deduplication behavior with an in-memory store.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="ttl">How long to remember processed event IDs. Default is 1 hour.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEventDeduplication(this IServiceCollection services, TimeSpan? ttl = null)
        {
            services.AddSingleton<IDeduplicationStore>(new InMemoryDeduplicationStore(ttl));
            services.AddEventPipelineBehavior<DeduplicationBehavior>();
            return services;
        }

        /// <summary>
        /// Registers a custom deduplication store.
        /// </summary>
        /// <typeparam name="TStore">The deduplication store type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddEventDeduplication<TStore>(this IServiceCollection services)
            where TStore : class, IDeduplicationStore
        {
            services.AddSingleton<IDeduplicationStore, TStore>();
            services.AddEventPipelineBehavior<DeduplicationBehavior>();
            return services;
        }
    }
}
