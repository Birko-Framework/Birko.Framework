using Birko.MessageQueue.InMemory;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.InMemory
{
    public class InMemoryMessageQueueTests
    {
        [Fact]
        public async Task ConnectAsync_SetsIsConnected()
        {
            using var queue = new InMemoryMessageQueue();

            queue.IsConnected.Should().BeFalse();
            await queue.ConnectAsync();
            queue.IsConnected.Should().BeTrue();
        }

        [Fact]
        public async Task DisconnectAsync_ClearsIsConnected()
        {
            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();
            await queue.DisconnectAsync();

            queue.IsConnected.Should().BeFalse();
        }

        [Fact]
        public async Task Dispose_ClearsIsConnected()
        {
            var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();
            queue.Dispose();

            queue.IsConnected.Should().BeFalse();
        }

        [Fact]
        public void Producer_IsNotNull()
        {
            using var queue = new InMemoryMessageQueue();
            queue.Producer.Should().NotBeNull();
        }

        [Fact]
        public void Consumer_IsNotNull()
        {
            using var queue = new InMemoryMessageQueue();
            queue.Consumer.Should().NotBeNull();
        }

        [Fact]
        public async Task SendAndSubscribe_DeliversMessage()
        {
            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();

            QueueMessage? received = null;
            var tcs = new TaskCompletionSource<QueueMessage>();

            await queue.Consumer.SubscribeAsync("test-topic", async (msg, ct) =>
            {
                tcs.TrySetResult(msg);
                await Task.CompletedTask;
            });

            await queue.Producer.SendAsync("test-topic", new QueueMessage { Body = "hello" }, CancellationToken.None);

            received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            received.Should().NotBeNull();
            received!.Body.Should().Be("hello");
        }

        [Fact]
        public async Task SendTyped_DeliversDeserializedPayload()
        {
            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();

            var tcs = new TaskCompletionSource<QueueMessage>();

            await queue.Consumer.SubscribeAsync("orders", async (msg, ct) =>
            {
                tcs.TrySetResult(msg);
                await Task.CompletedTask;
            });

            await queue.Producer.SendAsync("orders", new OrderPayload { OrderId = "ORD-001", Total = 99.99m });

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            received.Body.Should().Contain("ORD-001");

            var serializer = new JsonMessageSerializer();
            var payload = serializer.Deserialize<OrderPayload>(received.Body);
            payload.Should().NotBeNull();
            payload!.OrderId.Should().Be("ORD-001");
            payload.Total.Should().Be(99.99m);
        }

        [Fact]
        public async Task MultipleSubscribers_AllReceiveMessage()
        {
            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();

            var count = 0;
            var tcs = new TaskCompletionSource();

            await queue.Consumer.SubscribeAsync("topic", async (msg, ct) =>
            {
                Interlocked.Increment(ref count);
                if (count >= 2) tcs.TrySetResult();
                await Task.CompletedTask;
            });

            await queue.Consumer.SubscribeAsync("topic", async (msg, ct) =>
            {
                Interlocked.Increment(ref count);
                if (count >= 2) tcs.TrySetResult();
                await Task.CompletedTask;
            });

            await queue.Producer.SendAsync("topic", new QueueMessage { Body = "broadcast" }, CancellationToken.None);

            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            count.Should().Be(2);
        }

        [Fact]
        public async Task Unsubscribe_StopsDelivery()
        {
            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();

            var count = 0;
            var sub = await queue.Consumer.SubscribeAsync("topic", async (msg, ct) =>
            {
                Interlocked.Increment(ref count);
                await Task.CompletedTask;
            });

            await queue.Producer.SendAsync("topic", new QueueMessage { Body = "first" }, CancellationToken.None);
            await Task.Delay(100);

            await sub.UnsubscribeAsync();
            sub.IsActive.Should().BeFalse();

            await queue.Producer.SendAsync("topic", new QueueMessage { Body = "second" }, CancellationToken.None);
            await Task.Delay(100);

            count.Should().Be(1);
        }

        [Fact]
        public async Task ManualAck_TracksMessages()
        {
            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();

            var tcs = new TaskCompletionSource<QueueMessage>();

            await queue.Consumer.SubscribeAsync("ack-topic", async (msg, ct) =>
            {
                tcs.TrySetResult(msg);
                await Task.CompletedTask;
            }, new ConsumerOptions { AckMode = MessageAckMode.ManualAck });

            await queue.Producer.SendAsync("ack-topic", new QueueMessage { Body = "needs-ack" }, CancellationToken.None);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await queue.Consumer.AcknowledgeAsync(received.Id);
            // Should not throw
        }
    }

    public class OrderPayload
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Total { get; set; }
    }
}
