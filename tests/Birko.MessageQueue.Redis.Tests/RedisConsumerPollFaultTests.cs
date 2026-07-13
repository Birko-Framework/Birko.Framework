using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue;
using Birko.MessageQueue.Redis;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Birko.MessageQueue.Redis.Tests
{
    /// <summary>
    /// CR-M207: a background poll loop must not die silently on a non-cancellation fault — a
    /// per-iteration fault is surfaced via PollError and the loop keeps polling; a fault that
    /// terminates the loop is surfaced and the subscription deactivates.
    /// CR-M208: a configured ConsumerName must still be made unique per subscription.
    /// Driven over a mocked IDatabase via the internal test-seam constructor.
    /// </summary>
    public class RedisConsumerPollFaultTests
    {
        private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (condition()) return true;
                await Task.Delay(10).ConfigureAwait(false);
            }
            return condition();
        }

        private static Mock<IDatabase> DbWithGroup()
        {
            var db = new Mock<IDatabase>();
            db.Setup(d => d.StreamCreateConsumerGroupAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue?>(),
                    It.IsAny<bool>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            return db;
        }

        private static RedisStreamSettings Settings(string? consumerName = "c") => new()
        {
            ConsumerGroup = "g",
            ConsumerName = consumerName,
            BlockMilliseconds = 20,
        };

        [Fact]
        public async Task PollLoop_NonRedisFault_FiresPollError_AndKeepsPolling()
        {
            var db = DbWithGroup();
            db.Setup(d => d.StreamReadGroupAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                    It.IsAny<RedisValue?>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            using var consumer = new RedisConsumer(() => db.Object, new JsonMessageSerializer(), Settings());
            Exception? captured = null;
            consumer.PollError += (_, ex) => captured = ex;

            var sub = await consumer.SubscribeAsync("dest", (_, _) => Task.CompletedTask);

            (await WaitForAsync(() => captured != null)).Should().BeTrue("a non-Redis in-loop fault must surface via PollError");
            captured.Should().BeOfType<InvalidOperationException>();
            sub.IsActive.Should().BeTrue("an in-loop fault surfaces but the loop keeps polling");
        }

        [Fact]
        public async Task PollLoop_SetupFault_FiresPollError_AndDeactivatesSubscription()
        {
            var db = new Mock<IDatabase>();
            db.Setup(d => d.StreamCreateConsumerGroupAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue?>(),
                    It.IsAny<bool>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new InvalidOperationException("setup boom"));

            using var consumer = new RedisConsumer(() => db.Object, new JsonMessageSerializer(), Settings());
            Exception? captured = null;
            consumer.PollError += (_, ex) => captured = ex;

            var sub = await consumer.SubscribeAsync("dest", (_, _) => Task.CompletedTask);

            (await WaitForAsync(() => captured != null && !sub.IsActive)).Should().BeTrue(
                "a setup fault that terminates the loop must surface AND deactivate the subscription");
            captured.Should().BeOfType<InvalidOperationException>();
            sub.IsActive.Should().BeFalse();
        }

        [Fact]
        public async Task PollLoop_AutoAck_DeliversMessageAndAcknowledges()
        {
            // CR-M209: exercise the AutoAck poll-loop round-trip end-to-end over the mock seam —
            // StreamReadGroupAsync delivers one entry, the handler receives it, and it is XACK'd.
            var db = DbWithGroup();
            var entry = new StreamEntry("5-0", new[]
            {
                new NameValueEntry("body", "hello"),
                new NameValueEntry("id", Guid.NewGuid().ToString()),
            });
            var served = 0;
            db.Setup(d => d.StreamReadGroupAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                    It.IsAny<RedisValue?>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(() => Interlocked.Increment(ref served) == 1 ? new[] { entry } : Array.Empty<StreamEntry>());

            var acked = false;
            db.Setup(d => d.StreamAcknowledgeAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), (RedisValue)"5-0", It.IsAny<CommandFlags>()))
                .Callback(() => acked = true)
                .ReturnsAsync(1L);

            using var consumer = new RedisConsumer(() => db.Object, new JsonMessageSerializer(), Settings());
            QueueMessage? received = null;
            await consumer.SubscribeAsync("dest", (m, _) => { received = m; return Task.CompletedTask; },
                new ConsumerOptions { AckMode = MessageAckMode.AutoAck });

            (await WaitForAsync(() => received != null && acked)).Should().BeTrue();
            received!.Body.Should().Be("hello");
        }

        [Fact]
        public async Task Subscribe_ConfiguredConsumerName_IsUniquePerSubscription()
        {
            var db = DbWithGroup();
            var names = new ConcurrentBag<string>();
            db.Setup(d => d.StreamReadGroupAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                    It.IsAny<RedisValue?>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CommandFlags>()))
                .Callback((RedisKey _, RedisValue _, RedisValue consumer, RedisValue? _, int? _, bool _, CommandFlags _) => names.Add(consumer.ToString()))
                .ReturnsAsync(Array.Empty<StreamEntry>());

            using var consumer = new RedisConsumer(() => db.Object, new JsonMessageSerializer(), Settings("fixed"));

            await consumer.SubscribeAsync("dest", (_, _) => Task.CompletedTask);
            await consumer.SubscribeAsync("dest", (_, _) => Task.CompletedTask);

            (await WaitForAsync(() => names.Distinct().Count() >= 2)).Should().BeTrue();
            names.Should().OnlyContain(n => n.StartsWith("fixed-"), "the configured name is kept as a prefix");
            names.Distinct().Count().Should().BeGreaterThanOrEqualTo(2, "each subscription gets a distinct consumer identity");
        }
    }
}
