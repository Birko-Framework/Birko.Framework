using System;
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
    public class DistributedEventBusTests
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
        public async Task PublishAsync_SendsToMessageQueue()
        {
            using var queue = new InMemoryMessageQueue();

            string? receivedBody = null;
            await queue.Consumer.SubscribeAsync("events.order-placed", async (msg, ct) =>
            {
                receivedBody = msg.Body;
            });

            using var bus = new DistributedEventBus(queue);
            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 99.99m));

            await WaitUntilAsync(() => receivedBody != null);

            receivedBody.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task PublishAsync_SerializesEventEnvelope()
        {
            using var queue = new InMemoryMessageQueue();

            string? body = null;
            await queue.Consumer.SubscribeAsync("events.order-placed", async (msg, ct) =>
            {
                body = msg.Body;
            });

            using var bus = new DistributedEventBus(queue);
            var orderId = Guid.NewGuid();
            await bus.PublishAsync(new OrderPlaced(orderId, 50m));

            await WaitUntilAsync(() => body != null);

            body.Should().Contain("eventType");
            body.Should().Contain("payload");
            body.Should().Contain(orderId.ToString());
        }

        [Fact]
        public async Task SubscribeToTransportAsync_ReceivesAndDispatchesEvents()
        {
            using var queue = new InMemoryMessageQueue();
            var handler = new OrderPlacedHandler();

            using var bus = new DistributedEventBus(queue);
            bus.Subscribe(handler);
            await bus.SubscribeToTransportAsync<OrderPlaced>();

            var evt = new OrderPlaced(Guid.NewGuid(), 42m);
            await bus.PublishAsync(evt);

            await WaitUntilAsync(() => handler.ReceivedEvents.Count > 0);

            handler.ReceivedEvents.Should().ContainSingle()
                .Which.OrderId.Should().Be(evt.OrderId);
        }

        [Fact]
        public async Task SubscribeToTransportAsync_PassesCorrectContext()
        {
            using var queue = new InMemoryMessageQueue();
            var handler = new ContextCapturingHandler();

            using var bus = new DistributedEventBus(queue);
            bus.Subscribe(handler);
            await bus.SubscribeToTransportAsync<OrderPlaced>();

            var correlationId = Guid.NewGuid();
            var evt = new OrderPlaced(Guid.NewGuid(), 1m) { CorrelationId = correlationId };
            await bus.PublishAsync(evt);

            await WaitUntilAsync(() => handler.CapturedContext != null);

            handler.CapturedContext.Should().NotBeNull();
            handler.CapturedContext!.EventId.Should().Be(evt.EventId);
            handler.CapturedContext.Source.Should().Be("orders");
            handler.CapturedContext.CorrelationId.Should().Be(correlationId);
        }

        [Fact]
        public async Task Subscribe_MultipleHandlers_AllReceiveTransportEvents()
        {
            using var queue = new InMemoryMessageQueue();
            var handler1 = new OrderPlacedHandler();
            var handler2 = new SecondOrderHandler();

            using var bus = new DistributedEventBus(queue);
            bus.Subscribe(handler1);
            bus.Subscribe(handler2);
            await bus.SubscribeToTransportAsync<OrderPlaced>();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            await WaitUntilAsync(() => handler1.ReceivedEvents.Count > 0 && handler2.ReceivedEvents.Count > 0);

            handler1.ReceivedEvents.Should().ContainSingle();
            handler2.ReceivedEvents.Should().ContainSingle();
        }

        [Fact]
        public async Task DifferentEventTypes_RoutedToCorrectTopics()
        {
            using var queue = new InMemoryMessageQueue();
            var orderHandler = new OrderPlacedHandler();

            using var bus = new DistributedEventBus(queue);
            bus.Subscribe(orderHandler);
            await bus.SubscribeToTransportAsync<OrderPlaced>();

            await bus.PublishAsync(new DeviceOffline(Guid.NewGuid()));
            await Task.Delay(200);

            orderHandler.ReceivedEvents.Should().BeEmpty();
        }

        [Fact]
        public void Subscribe_ReturnsActiveSubscription()
        {
            using var queue = new InMemoryMessageQueue();
            using var bus = new DistributedEventBus(queue);

            var sub = bus.Subscribe(new OrderPlacedHandler());

            sub.IsActive.Should().BeTrue();
            sub.EventType.Should().Be(typeof(OrderPlaced));
        }

        [Fact]
        public async Task Subscribe_Dispose_Unsubscribes()
        {
            using var queue = new InMemoryMessageQueue();
            var handler = new OrderPlacedHandler();

            using var bus = new DistributedEventBus(queue);
            var sub = bus.Subscribe(handler);
            await bus.SubscribeToTransportAsync<OrderPlaced>();

            sub.Dispose();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
            await Task.Delay(200);

            handler.ReceivedEvents.Should().BeEmpty();
        }
    }
}
