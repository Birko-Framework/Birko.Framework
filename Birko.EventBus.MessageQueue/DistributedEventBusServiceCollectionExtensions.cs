using System;
using Birko.EventBus.Enrichment;
using Birko.EventBus.Pipeline;
using Birko.MessageQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Birko.EventBus.MessageQueue
{
    /// <summary>
    /// DI registration extensions for distributed event bus.
    /// </summary>
    public static class DistributedEventBusServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the distributed event bus backed by an <see cref="IMessageQueue"/> provider.
        /// The message queue must already be registered in DI.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional options configuration.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDistributedEventBus(this IServiceCollection services, Action<DistributedEventBusOptions>? configure = null)
        {
            var options = new DistributedEventBusOptions();
            configure?.Invoke(options);

            services.AddSingleton(options);
            // SH-M114: the bus is its own singleton, and IEventBus resolves to it. The hosted service takes the bus by
            // its concrete type, so a decorator over IEventBus (AddOutboxEventBus) no longer hides it: the hosted service
            // cast IEventBus to DistributedEventBus and threw at startup once the outbox wrapped it.
            services.AddSingleton(sp =>
            {
                var messageQueue = sp.GetRequiredService<IMessageQueue>();
                var behaviors = sp.GetServices<IEventPipelineBehavior>();
                var enrichers = sp.GetServices<IEventEnricher>();
                return new DistributedEventBus(messageQueue, options, sp, behaviors, enrichers);
            });
            services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<DistributedEventBus>());

            // SH-L112: always registered; StartAsync reads AutoSubscribe when the host starts, so a change to the
            // registered options before then is honoured. Deciding here, at registration, ignored it.
            services.AddSingleton<IHostedService, DistributedEventBusHostedService>();

            return services;
        }
    }
}
