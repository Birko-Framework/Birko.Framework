using System;
using System.Threading.Tasks;
using Birko.EventBus.Local;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Extensions;
using Birko.EventBus.Outbox.Publishing;
using Birko.EventBus.Outbox.Stores;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.EventBus.Tests.Outbox
{
    /// <summary>
    /// CR-L260: the OutboxProcessor DI factory must publish through the real inner bus (unwrapped from the
    /// OutboxEventBus decorator), never back through the outbox — otherwise processing would loop entries
    /// into the store. Because the factory resolves IEventBus lazily, this holds regardless of whether
    /// AddOutbox or AddOutboxEventBus is registered first.
    /// </summary>
    public class OutboxDiRegistrationTests
    {
        [Theory]
        [InlineData(true)]  // AddOutboxEventBus before AddOutbox
        [InlineData(false)] // AddOutbox before AddOutboxEventBus
        public async Task Processor_PublishesThroughInnerBus_RegardlessOfRegistrationOrder(bool eventBusFirst)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IEventBus, InProcessEventBus>();

            if (eventBusFirst)
            {
                services.AddOutboxEventBus();
                services.AddInMemoryOutbox();
            }
            else
            {
                services.AddInMemoryOutbox();
                services.AddOutboxEventBus();
            }

            using var provider = services.BuildServiceProvider();

            var bus = provider.GetRequiredService<IEventBus>();
            bus.Should().BeOfType<OutboxEventBus>("AddOutboxEventBus decorates the registered bus");

            var handler = new OrderPlacedHandler();
            bus.Subscribe(handler); // delegates to the inner InProcessEventBus

            var evt = new OrderPlaced(Guid.NewGuid(), 5m);
            await bus.PublishAsync(evt);
            handler.ReceivedEvents.Should().BeEmpty("publishing only writes to the outbox store, not the bus");

            var processor = provider.GetRequiredService<OutboxProcessor>();
            var count = await processor.ProcessBatchAsync();

            count.Should().Be(1);
            handler.ReceivedEvents.Should().ContainSingle(
                "the processor must publish through the inner bus exactly once (no loop back into the outbox)")
                .Which.OrderId.Should().Be(evt.OrderId);

            var store = (InMemoryOutboxStore)provider.GetRequiredService<IOutboxStore>();
            store.GetAll().Should().ContainSingle(
                "processing must not create a new pending entry")
                .Which.Status.Should().Be(OutboxStatus.Published);
        }
    }
}
