using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.MessageQueue;
using Birko.EventBus.Tests.TestResources;
using Birko.MessageQueue.InMemory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.EventBus.Tests.MessageQueue
{
    /// <summary>
    /// CR-L258: AutoSubscriber (DI discovery via reflection) had no direct coverage. These pin that
    /// SubscribeAllAsync creates a transport subscription for a DI-registered handler type (so published
    /// events reach it), and that a handler type NOT registered in DI gets no subscription — which also
    /// demonstrates CR-L256 (a manual Subscribe alone, with no transport subscription, receives nothing).
    /// </summary>
    public class AutoSubscriberTests
    {
        private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000, int pollMs = 25)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(pollMs);
            }
        }

        [Fact]
        public async Task SubscribeAllAsync_DiscoversRegisteredHandler_AndDispatchesPublishedEvent()
        {
            using var queue = new InMemoryMessageQueue();
            var services = new ServiceCollection();
            services.AddSingleton<IEventHandler<OrderPlaced>, OrderPlacedHandler>();
            using var provider = services.BuildServiceProvider();

            using var bus = new DistributedEventBus(queue, serviceProvider: provider);
            var subscriber = new AutoSubscriber(bus, provider);
            await subscriber.SubscribeAllAsync();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 5m));

            var handler = (OrderPlacedHandler)provider.GetServices<IEventHandler<OrderPlaced>>().First();
            await WaitUntilAsync(() => handler.ReceivedEvents.Count > 0);

            handler.ReceivedEvents.Should().ContainSingle(
                "AutoSubscriber must create a transport subscription for the DI-registered handler type");
        }

        [Fact]
        public async Task SubscribeAllAsync_HandlerTypeNotRegisteredInDi_CreatesNoSubscription()
        {
            using var queue = new InMemoryMessageQueue();
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider(); // nothing registered

            using var bus = new DistributedEventBus(queue, serviceProvider: provider);
            // A manual subscription exists, but AutoSubscriber won't create the transport subscription
            // because no IEventHandler<OrderPlaced> is registered in DI (CR-L256: Subscribe alone is inert).
            var manual = new OrderPlacedHandler();
            bus.Subscribe(manual);

            var subscriber = new AutoSubscriber(bus, provider);
            await subscriber.SubscribeAllAsync();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 5m));
            await Task.Delay(200);

            manual.ReceivedEvents.Should().BeEmpty(
                "with no transport subscription the manual handler is never invoked");
        }
    }
}
