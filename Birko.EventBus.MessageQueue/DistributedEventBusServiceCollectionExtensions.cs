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
            services.AddSingleton<IEventBus>(sp =>
            {
                var messageQueue = sp.GetRequiredService<IMessageQueue>();
                var behaviors = sp.GetServices<IEventPipelineBehavior>();
                var enrichers = sp.GetServices<IEventEnricher>();
                return new DistributedEventBus(messageQueue, options, sp, behaviors, enrichers);
            });

            if (options.AutoSubscribe)
            {
                services.AddSingleton<IHostedService, DistributedEventBusHostedService>();
            }

            return services;
        }
    }
}
