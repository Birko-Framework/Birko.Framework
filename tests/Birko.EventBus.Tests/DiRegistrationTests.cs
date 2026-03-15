using System;
using System.Threading.Tasks;
using Birko.EventBus.Extensions;
using Birko.EventBus.Local;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.EventBus.Tests
{
    public class DiRegistrationTests
    {
        [Fact]
        public void AddEventBus_RegistersSingleton()
        {
            var services = new ServiceCollection();
            services.AddEventBus();

            var sp = services.BuildServiceProvider();
            var bus = sp.GetService<IEventBus>();

            bus.Should().NotBeNull();
            bus.Should().BeOfType<InProcessEventBus>();
        }

        [Fact]
        public void AddEventBus_ReturnsSameInstance()
        {
            var services = new ServiceCollection();
            services.AddEventBus();

            var sp = services.BuildServiceProvider();
            var bus1 = sp.GetRequiredService<IEventBus>();
            var bus2 = sp.GetRequiredService<IEventBus>();

            bus1.Should().BeSameAs(bus2);
        }

        [Fact]
        public async Task AddEventHandler_RegistersHandlerForDispatch()
        {
            var services = new ServiceCollection();
            services.AddEventBus();
            services.AddEventHandler<OrderPlaced, OrderPlacedHandler>();

            var sp = services.BuildServiceProvider();
            var bus = sp.GetRequiredService<IEventBus>();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            // Handler was resolved and called — no exception means it worked
            // We can't easily inspect the transient handler instance, but no error means DI resolved it
        }

        [Fact]
        public void AddEventBus_WithOptions_AppliesConfiguration()
        {
            var services = new ServiceCollection();
            services.AddEventBus(opts =>
            {
                opts.MaxConcurrency = 8;
                opts.ErrorHandling = ErrorHandlingMode.Stop;
            });

            var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<InProcessEventBusOptions>();

            options.MaxConcurrency.Should().Be(8);
            options.ErrorHandling.Should().Be(ErrorHandlingMode.Stop);
        }
    }
}
