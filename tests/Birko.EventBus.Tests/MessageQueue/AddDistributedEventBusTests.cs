using System.Linq;
using Birko.EventBus.MessageQueue;
using Birko.MessageQueue;
using Birko.MessageQueue.InMemory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Birko.EventBus.Tests.MessageQueue
{
    /// <summary>
    /// CR-L258: the AddDistributedEventBus DI extension had no coverage — pins the singleton IEventBus /
    /// options wiring and the AutoSubscribe-conditional IHostedService registration.
    /// </summary>
    public class AddDistributedEventBusTests
    {
        private static ServiceCollection ServicesWithQueue()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IMessageQueue>(new InMemoryMessageQueue());
            return services;
        }

        [Fact]
        public void AddDistributedEventBus_RegistersSingletonBusAndOptions()
        {
            var services = ServicesWithQueue();

            services.AddDistributedEventBus();

            using var provider = services.BuildServiceProvider();
            var bus1 = provider.GetRequiredService<IEventBus>();
            var bus2 = provider.GetRequiredService<IEventBus>();

            bus1.Should().BeOfType<DistributedEventBus>();
            bus1.Should().BeSameAs(bus2, "the bus is registered as a singleton");
            provider.GetService<DistributedEventBusOptions>().Should().NotBeNull();
        }

        [Fact]
        public void AddDistributedEventBus_AutoSubscribeTrue_RegistersHostedService()
        {
            var services = ServicesWithQueue();

            services.AddDistributedEventBus(o => o.AutoSubscribe = true);

            using var provider = services.BuildServiceProvider();
            provider.GetServices<IHostedService>()
                .OfType<DistributedEventBusHostedService>()
                .Should().ContainSingle();
        }

        [Fact]
        public void AddDistributedEventBus_AutoSubscribeFalse_DoesNotRegisterHostedService()
        {
            var services = ServicesWithQueue();

            services.AddDistributedEventBus(o => o.AutoSubscribe = false);

            using var provider = services.BuildServiceProvider();
            provider.GetServices<IHostedService>()
                .OfType<DistributedEventBusHostedService>()
                .Should().BeEmpty();
        }
    }
}
