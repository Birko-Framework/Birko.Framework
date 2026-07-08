using System;
using System.Reflection;
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
    /// Regressions for CR-H121: failed / rejected messages must actually be redelivered during
    /// the subscription lifetime. Reclaim runs via XAUTOCLAIM over the Pending Entries List, and
    /// RejectAsync must honor the requeue flag (ACK when false, leave unacked when true).
    /// The consumer is driven over a mocked IDatabase via the internal test-seam constructor.
    /// </summary>
    public class RedisConsumerRedeliveryTests
    {
        private static StreamEntry Entry(string id, string body)
        {
            return new StreamEntry(id, new[]
            {
                new NameValueEntry("body", body),
                new NameValueEntry("id", Guid.NewGuid().ToString()),
            });
        }

        private static StreamAutoClaimResult AutoClaimResult(params StreamEntry[] entries)
        {
            // StreamAutoClaimResult has only a non-public ctor (RedisValue, StreamEntry[], RedisValue[]).
            var ctor = typeof(StreamAutoClaimResult).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic)[0];
            return (StreamAutoClaimResult)ctor.Invoke(new object[]
            {
                (RedisValue)"0-0", entries, Array.Empty<RedisValue>(),
            });
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(10).ConfigureAwait(false);
            }

            return condition();
        }

        private static RedisStreamSettings Settings(long pendingRetryMs) => new()
        {
            ConsumerGroup = "g",
            ConsumerName = "c",
            BlockMilliseconds = 20,
            PendingRetryMilliseconds = pendingRetryMs,
        };

        [Fact]
        public async Task Reclaim_RedeliversPendingEntry_AndAutoAcks()
        {
            var db = new Mock<IDatabase>();
            db.Setup(d => d.StreamCreateConsumerGroupAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue?>(),
                    It.IsAny<bool>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            // No new messages — the idle branch fires the reclaim pass.
            db.Setup(d => d.StreamReadGroupAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                    It.IsAny<RedisValue?>(), It.IsAny<int?>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(Array.Empty<StreamEntry>());

            var claimCalls = 0;
            db.Setup(d => d.StreamAutoClaimAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                    It.IsAny<long>(), It.IsAny<RedisValue>(), It.IsAny<int?>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(() => Interlocked.Increment(ref claimCalls) == 1
                    ? AutoClaimResult(Entry("5-0", "reclaimed-body"))
                    : AutoClaimResult());

            var acked = false;
            db.Setup(d => d.StreamAcknowledgeAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Callback(() => acked = true)
                .ReturnsAsync(1L);

            using var consumer = new RedisConsumer(() => db.Object, new JsonMessageSerializer(), Settings(1));

            QueueMessage? received = null;
            await consumer.SubscribeAsync("dest", (msg, _) => { received = msg; return Task.CompletedTask; },
                new ConsumerOptions { AckMode = MessageAckMode.AutoAck });

            (await WaitForAsync(() => received != null && acked)).Should().BeTrue("the reclaimed entry should be redelivered and auto-acked");
            received!.Body.Should().Be("reclaimed-body");

            db.Verify(d => d.StreamAutoClaimAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                It.IsAny<long>(), It.IsAny<RedisValue>(), It.IsAny<int?>(), It.IsAny<CommandFlags>()), Times.AtLeastOnce);
            db.Verify(d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), (RedisValue)"5-0", It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task RejectAsync_NoRequeue_AcknowledgesEntry()
        {
            var db = new Mock<IDatabase>();
            db.Setup(d => d.StreamAcknowledgeAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(1L);

            using var consumer = new RedisConsumer(() => db.Object, new JsonMessageSerializer(), Settings(0));
            var messageId = InjectPending(consumer, "stream", "g", "7-0");

            await consumer.RejectAsync(messageId, requeue: false);

            // No requeue → the entry is XACK'd so it leaves the PEL and is not re-delivered.
            db.Verify(d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(), (RedisValue)"g", (RedisValue)"7-0", It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task RejectAsync_Requeue_DoesNotAcknowledge()
        {
            var db = new Mock<IDatabase>(MockBehavior.Strict);

            using var consumer = new RedisConsumer(() => db.Object, new JsonMessageSerializer(), Settings(0));
            var messageId = InjectPending(consumer, "stream", "g", "7-0");

            await consumer.RejectAsync(messageId, requeue: true);

            // requeue:true leaves the entry unacked so the reclaim pass can redeliver it —
            // the strict mock proves the database is not touched at all.
            db.Verify(d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        // Injects a tracked pending message (as the poll loop would under ManualAck) so RejectAsync
        // can be exercised deterministically without driving the background poll loop.
        private static Guid InjectPending(RedisConsumer consumer, string streamKey, string group, string entryId)
        {
            var messageId = Guid.NewGuid();
            var field = typeof(RedisConsumer).GetField("_pendingAck", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var dict = (System.Collections.IDictionary)field.GetValue(consumer)!;
            var pmType = typeof(RedisConsumer).GetNestedType("PendingMessage", BindingFlags.NonPublic)!;
            var pm = Activator.CreateInstance(pmType)!;
            pmType.GetProperty("StreamKey")!.SetValue(pm, streamKey);
            pmType.GetProperty("ConsumerGroup")!.SetValue(pm, group);
            pmType.GetProperty("StreamEntryId")!.SetValue(pm, (RedisValue)entryId);
            dict[messageId] = pm;
            return messageId;
        }

        [Fact]
        public void PendingRetryMilliseconds_DefaultsTo30Seconds()
        {
            new RedisStreamSettings().PendingRetryMilliseconds.Should().Be(30_000);
        }

        [Fact]
        public async Task RejectAsync_UnknownMessageId_DoesNotThrowOrTouchDatabase()
        {
            var db = new Mock<IDatabase>(MockBehavior.Strict);
            using var consumer = new RedisConsumer(() => db.Object, new JsonMessageSerializer(), Settings(0));

            var act = () => consumer.RejectAsync(Guid.NewGuid(), requeue: false);

            await act.Should().NotThrowAsync();
        }
    }
}
