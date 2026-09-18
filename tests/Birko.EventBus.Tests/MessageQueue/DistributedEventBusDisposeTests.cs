using System;
using System.Threading.Tasks;
using Birko.EventBus.MessageQueue;
using Birko.EventBus.Tests.TestResources;
using Birko.MessageQueue.InMemory;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests.MessageQueue
{
    /// <summary>
    /// CR-M188: Dispose() blocked on `sub.UnsubscribeAsync().GetAwaiter().GetResult()` (sync-over-async,
    /// deadlock-prone on a sync context). The bus now implements IAsyncDisposable (awaits UnsubscribeAsync)
    /// and the synchronous Dispose() uses ISubscription.Dispose() instead of blocking.
    /// </summary>
    public class DistributedEventBusDisposeTests
    {
        [Fact]
        public void ImplementsIAsyncDisposable()
        {
            using var queue = new InMemoryMessageQueue();
            using var bus = new DistributedEventBus(queue);
            bus.Should().BeAssignableTo<IAsyncDisposable>();
        }

        [Fact]
        public async Task DisposeAsync_AfterTransportSubscribe_Completes_AndPreventsPublish()
        {
            var queue = new InMemoryMessageQueue();
            var bus = new DistributedEventBus(queue);
            bus.Subscribe(new OrderPlacedHandler());
            await bus.SubscribeToTransportAsync<OrderPlaced>();

            await bus.DisposeAsync(); // awaits UnsubscribeAsync — must not throw/hang

            await bus.Invoking(b => b.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m)))
                .Should().ThrowAsync<ObjectDisposedException>();
            queue.Dispose();
        }

        [Fact]
        public async Task Dispose_AfterTransportSubscribe_DoesNotBlockOrThrow()
        {
            var queue = new InMemoryMessageQueue();
            var bus = new DistributedEventBus(queue);
            bus.Subscribe(new OrderPlacedHandler());
            await bus.SubscribeToTransportAsync<OrderPlaced>();

            // Synchronous Dispose now calls ISubscription.Dispose() rather than blocking on the async
            // unsubscribe — it returns without deadlock.
            var dispose = () => bus.Dispose();
            dispose.Should().NotThrow();

            await bus.Invoking(b => b.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m)))
                .Should().ThrowAsync<ObjectDisposedException>();
            queue.Dispose();
        }
    }
}
