using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Birko.EventBus.MessageQueue
{
    /// <summary>
    /// Hosted service that starts the <see cref="AutoSubscriber"/> on application startup.
    /// Creates transport-level subscriptions for all discovered IEventHandler&lt;T&gt; registrations.
    /// </summary>
    public class DistributedEventBusHostedService : IHostedService
    {
        private readonly DistributedEventBus _bus;
        private readonly IServiceProvider _serviceProvider;
        private readonly DistributedEventBusOptions _options;

        public DistributedEventBusHostedService(
            DistributedEventBus bus,
            IServiceProvider serviceProvider,
            DistributedEventBusOptions options)
        {
            // SH-M114: the concrete bus, registered on its own by AddDistributedEventBus, whatever decorates IEventBus.
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _serviceProvider = serviceProvider;
            _options = options;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_options.AutoSubscribe)
            {
                return;
            }

            var subscriber = new AutoSubscriber(_bus, _serviceProvider);
            await subscriber.SubscribeAllAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
