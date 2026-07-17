using System;
using Birko.EventBus.Outbox.Hosting;
using Birko.EventBus.Outbox.Publishing;
using Birko.EventBus.Outbox.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Birko.EventBus.Outbox.Extensions
{
    /// <summary>
    /// DI registration extensions for the outbox pattern.
    /// </summary>
    public static class OutboxServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the outbox pattern with a custom outbox store.
        /// Registers <see cref="OutboxProcessor"/> and <see cref="OutboxProcessorHostedService"/>.
        /// </summary>
        /// <remarks>
        /// Pair this with <see cref="AddOutboxEventBus"/> (which decorates <see cref="IEventBus"/> so
        /// <c>PublishAsync</c> writes to the store instead of publishing directly). The order of the two
        /// calls does not matter: the processor factory resolves <see cref="IEventBus"/> lazily and unwraps
        /// the <see cref="OutboxEventBus"/> decorator to its inner bus, so the processor always publishes
        /// through the real transport — never back through the outbox (which would loop entries into the
        /// store). If <see cref="AddOutboxEventBus"/> is not registered at all, nothing writes to the outbox
        /// and the processor simply has no pending entries to publish.
        /// </remarks>
        /// <typeparam name="TStore">The outbox store implementation type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional options configuration.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddOutbox<TStore>(this IServiceCollection services, Action<OutboxOptions>? configure = null)
            where TStore : class, IOutboxStore
        {
            var options = new OutboxOptions();
            configure?.Invoke(options);

            services.AddSingleton(options);
            services.AddSingleton<IOutboxStore, TStore>();
            services.AddSingleton<OutboxProcessor>(sp =>
            {
                // Resolve the registered bus once, then unwrap the OutboxEventBus decorator (if present) so
                // the processor publishes through the real inner transport rather than looping back into the
                // outbox store (CR-L260).
                var bus = sp.GetRequiredService<IEventBus>();
                var innerBus = bus is OutboxEventBus outboxBus ? outboxBus.Inner : bus;
                var store = sp.GetRequiredService<IOutboxStore>();
                // STORY-046: optional scope bridge — when registered (e.g. a tenant bridge), the processor
                // restores the entry's ambient scope before re-publishing. Absent → NullEventScopeAccessor.
                var scopeAccessor = sp.GetService<IEventScopeAccessor>();
                return new OutboxProcessor(store, innerBus, options, scopeAccessor: scopeAccessor);
            });
            services.AddSingleton<IHostedService, OutboxProcessorHostedService>();

            return services;
        }

        /// <summary>
        /// Registers the outbox pattern with the in-memory store (for testing/development).
        /// </summary>
        public static IServiceCollection AddInMemoryOutbox(this IServiceCollection services, Action<OutboxOptions>? configure = null)
        {
            return services.AddOutbox<InMemoryOutboxStore>(configure);
        }

        /// <summary>
        /// Wraps the existing IEventBus registration with <see cref="OutboxEventBus"/>.
        /// Must be called AFTER AddEventBus() or AddDistributedEventBus().
        /// </summary>
        public static IServiceCollection AddOutboxEventBus(this IServiceCollection services)
        {
            services.Decorate<IEventBus>((inner, sp) =>
            {
                var store = sp.GetRequiredService<IOutboxStore>();
                var enrichers = sp.GetServices<Birko.EventBus.Enrichment.IEventEnricher>();
                return new OutboxEventBus(inner, store, enrichers: enrichers);
            });

            return services;
        }
    }

    /// <summary>
    /// Extension method for decorator pattern on IServiceCollection.
    /// </summary>
    internal static class ServiceCollectionDecoratorExtensions
    {
        /// <summary>
        /// Decorates an existing service registration.
        /// </summary>
        public static IServiceCollection Decorate<TService>(
            this IServiceCollection services,
            Func<TService, IServiceProvider, TService> decorator)
            where TService : class
        {
            // Find the existing registration
            var existingDescriptor = FindDescriptor<TService>(services);
            if (existingDescriptor == null)
            {
                throw new InvalidOperationException(
                    $"No registration found for {typeof(TService).Name}. Register the inner service first.");
            }

            // Remove old registration
            services.Remove(existingDescriptor);

            // Add decorated registration
            services.Add(new ServiceDescriptor(
                typeof(TService),
                sp =>
                {
                    // Resolve the original implementation
                    TService inner;
                    if (existingDescriptor.ImplementationFactory != null)
                    {
                        inner = (TService)existingDescriptor.ImplementationFactory(sp);
                    }
                    else if (existingDescriptor.ImplementationInstance != null)
                    {
                        inner = (TService)existingDescriptor.ImplementationInstance;
                    }
                    else if (existingDescriptor.ImplementationType != null)
                    {
                        inner = (TService)ActivatorUtilities.CreateInstance(sp, existingDescriptor.ImplementationType);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Cannot resolve inner {typeof(TService).Name} for decoration.");
                    }

                    return decorator(inner, sp);
                },
                existingDescriptor.Lifetime));

            return services;
        }

        private static ServiceDescriptor? FindDescriptor<TService>(IServiceCollection services)
        {
            for (int i = services.Count - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == typeof(TService))
                {
                    return services[i];
                }
            }

            return null;
        }
    }
}
