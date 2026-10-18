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
    /// CR-L258: DistributedEventBusHostedService had no coverage — pins the AutoSubscribe=false
    /// short-circuit, the AutoSubscribe=true auto-subscription path, and the IEventBus -> DistributedEventBus
    /// cast-failure guard in the constructor.
    /// </summary>
    public class DistributedEventBusHostedServiceTests
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
        public void Constructor_NullBus_ThrowsArgumentNullException()
        {
            // TASK-155 (SH-M114) replaced the IEventBus -> DistributedEventBus cast guard this test pinned: the hosted
            // service now takes the concrete bus (registered on its own by AddDistributedEventBus), so a decorator over
            // IEventBus such as the outbox no longer makes the cast fail at startup. The type system enforces the rest.
            var services = new ServiceCollection();
            using var provider = services.BuildServiceProvider();

            Action act = () => new DistributedEventBusHostedService(null!, provider, new DistributedEventBusOptions());

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public async Task StartAsync_AutoSubscribeFalse_CreatesNoSubscription()
        {
            using var queue = new InMemoryMessageQueue();
            var services = new ServiceCollection();
            services.AddSingleton<IEventHandler<OrderPlaced>, OrderPlacedHandler>();
            using var provider = services.BuildServiceProvider();

            var options = new DistributedEventBusOptions { AutoSubscribe = false };
            using var bus = new DistributedEventBus(queue, options, provider);
            var svc = new DistributedEventBusHostedService(bus, provider, options);

            await svc.StartAsync(CancellationToken.None);

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
            await Task.Delay(200);

            var handler = (OrderPlacedHandler)provider.GetServices<IEventHandler<OrderPlaced>>().First();
            handler.ReceivedEvents.Should().BeEmpty(
                "AutoSubscribe=false must short-circuit before any transport subscription is created");
        }

        [Fact]
        public async Task StartAsync_AutoSubscribeTrue_SubscribesRegisteredHandler()
        {
            using var queue = new InMemoryMessageQueue();
            var services = new ServiceCollection();
            services.AddSingleton<IEventHandler<OrderPlaced>, OrderPlacedHandler>();
            using var provider = services.BuildServiceProvider();

            var options = new DistributedEventBusOptions { AutoSubscribe = true };
            using var bus = new DistributedEventBus(queue, options, provider);
            var svc = new DistributedEventBusHostedService(bus, provider, options);

            await svc.StartAsync(CancellationToken.None);
            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            var handler = (OrderPlacedHandler)provider.GetServices<IEventHandler<OrderPlaced>>().First();
            await WaitUntilAsync(() => handler.ReceivedEvents.Count > 0);

            handler.ReceivedEvents.Should().ContainSingle();

            await svc.StopAsync(CancellationToken.None); // no-op, must not throw
        }
    }
}
