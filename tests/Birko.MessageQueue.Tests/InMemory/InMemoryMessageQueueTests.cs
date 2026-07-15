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
        public async Task SendTyped_StampsSerializerContentType_WithoutCallerHeaders()
        {
            // CR-L284: the typed send always stamps the serializer's content type.
            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();
            var tcs = new TaskCompletionSource<QueueMessage>();
            await queue.Consumer.SubscribeAsync("orders", async (msg, ct) => { tcs.TrySetResult(msg); await Task.CompletedTask; });

            await queue.Producer.SendAsync("orders", new OrderPayload { OrderId = "X", Total = 1m });

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            received.Headers.Should().NotBeNull();
            received.Headers!.ContentType.Should().Be(new JsonMessageSerializer().ContentType);
        }

        [Fact]
        public async Task SendTyped_StampsSerializerContentType_OverridingCallerHeaders()
        {
            // CR-L284: caller-supplied headers still get the serializer's content type stamped.
            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();
            var tcs = new TaskCompletionSource<QueueMessage>();
            await queue.Consumer.SubscribeAsync("orders", async (msg, ct) => { tcs.TrySetResult(msg); await Task.CompletedTask; });

            await queue.Producer.SendAsync("orders", new OrderPayload { OrderId = "X", Total = 1m },
                new MessageHeaders { ContentType = "text/plain" });

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            received.Headers.Should().NotBeNull();
            received.Headers!.ContentType.Should().Be(new JsonMessageSerializer().ContentType,
                "the serializer's content type is stamped even when the caller passes headers");
        }

        [Fact]
        public async Task OptionsConstructor_ProducesWorkingQueue()
        {
            // CR-L283: the options ctor is wired in and yields a functional queue.
            using var queue = new InMemoryMessageQueue(new InMemoryMessageQueueOptions { ChannelCapacity = 8 });
            await queue.ConnectAsync();
            var tcs = new TaskCompletionSource<QueueMessage>();
            await queue.Consumer.SubscribeAsync("t", async (msg, ct) => { tcs.TrySetResult(msg); await Task.CompletedTask; });

            await queue.Producer.SendAsync("t", new QueueMessage { Body = "hi" }, CancellationToken.None);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            received.Body.Should().Be("hi");
        }

        [Fact]
        public void OptionsConstructor_NullOptions_ThrowsArgumentNullException()
        {
            var act = () => new InMemoryMessageQueue((InMemoryMessageQueueOptions)null!);

            act.Should().Throw<ArgumentNullException>().WithParameterName("options");
        }

        [Fact]
        public async Task DelayedSend_DeliversAfterDelay()
        {
            // CR-L287: the message.Delay path was untested.
            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();
            var tcs = new TaskCompletionSource<QueueMessage>();
            await queue.Consumer.SubscribeAsync("t", async (msg, ct) => { tcs.TrySetResult(msg); await Task.CompletedTask; });

            await queue.Producer.SendAsync("t",
                new QueueMessage { Body = "later", Delay = TimeSpan.FromMilliseconds(200) }, CancellationToken.None);

            tcs.Task.IsCompleted.Should().BeFalse("delivery is deferred until the delay elapses");

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            received.Body.Should().Be("later");
        }

        [Fact]
        public async Task Producer_SendAfterDispose_ThrowsObjectDisposedException()
        {
            // CR-L287: use-after-dispose was untested (queue.Dispose disposes the producer).
            var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();
            queue.Dispose();

            var act = async () => await queue.Producer.SendAsync("t", new QueueMessage { Body = "x" }, CancellationToken.None);

            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        [Fact]
        public async Task Dispatch_OneSubscriberThrows_OthersStillReceive()
        {
            // CR-L287: handler-failure isolation in the dispatch loop was untested.
            using var queue = new InMemoryMessageQueue();
            await queue.ConnectAsync();
            var tcs = new TaskCompletionSource<QueueMessage>();

            await queue.Consumer.SubscribeAsync("t", (msg, ct) => throw new InvalidOperationException("boom"));
            await queue.Consumer.SubscribeAsync("t", async (msg, ct) => { tcs.TrySetResult(msg); await Task.CompletedTask; });

            await queue.Producer.SendAsync("t", new QueueMessage { Body = "ok" }, CancellationToken.None);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
            received.Body.Should().Be("ok");
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
