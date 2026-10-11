using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    /// SH-H020 (TASK-318) — a subscription with no consumer group (the default: <c>ConsumerGroup</c> null,
    /// <c>FromBeginning</c> false) started its XREAD at <c>"$"</c>. StackExchange.Redis refuses that position for
    /// XREAD — measured against Redis 7 / SE.Redis 2.13.17:
    /// <c>InvalidOperationException: StreamPosition.NewMessages cannot be used with StreamRead.</c> — so every poll
    /// threw, the loop reported it through the <b>internal</b> <c>PollError</c> and retried forever, and no message
    /// was ever delivered while <c>IsActive</c> read true. The fake stream below reproduces that refusal, so these
    /// tests fail on the original code for the reason production did.
    ///
    /// <para>Same position tracking, found while verifying: an unparseable or expired entry did not advance the
    /// read position, so as the last entry of a batch it was re-read on every poll, with no delay.</para>
    /// </summary>
    public class RedisNonGroupReadPositionTests
    {
        private static readonly JsonMessageSerializer Serializer = new();

        /// <summary>An append-only stream with Redis's XREAD / XREVRANGE semantics over a mocked IDatabase.</summary>
        private sealed class FakeStream
        {
            private readonly List<StreamEntry> _entries = new();
            private long _next = 1;
            public Mock<IDatabase> Db { get; } = new();
            public ConcurrentQueue<string> ReadPositions { get; } = new();

            public FakeStream()
            {
                Db.Setup(d => d.StreamReadAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<int?>(), It.IsAny<CommandFlags>()))
                    .Returns<RedisKey, RedisValue, int?, CommandFlags>((_, position, count, _) =>
                    {
                        ReadPositions.Enqueue(position.ToString());
                        if (position == "$")
                        {
                            // What StackExchange.Redis does with "$" on XREAD, measured.
                            throw new InvalidOperationException("StreamPosition.NewMessages cannot be used with StreamRead.");
                        }
                        var after = Seq(position.ToString());
                        lock (_entries)
                        {
                            return Task.FromResult(_entries.Where(e => Seq(e.Id.ToString()) > after).Take(count ?? int.MaxValue).ToArray());
                        }
                    });
                Db.Setup(d => d.StreamRangeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue?>(), It.IsAny<RedisValue?>(),
                        It.IsAny<int?>(), It.IsAny<Order>(), It.IsAny<CommandFlags>()))
                    .Returns<RedisKey, RedisValue?, RedisValue?, int?, Order, CommandFlags>((_, _, _, count, order, _) =>
                    {
                        lock (_entries)
                        {
                            IEnumerable<StreamEntry> q = order == Order.Descending ? Enumerable.Reverse(_entries) : _entries;
                            return Task.FromResult(q.Take(count ?? int.MaxValue).ToArray());
                        }
                    });
            }

            private static long Seq(string id) => long.Parse(id.Split('-')[0]);

            public string Add(params NameValueEntry[] values)
            {
                lock (_entries)
                {
                    var id = $"{_next++}-0";
                    _entries.Add(new StreamEntry(id, values));
                    return id;
                }
            }

            public string AddMessage(string body) =>
                Add(new NameValueEntry("message", Serializer.Serialize(new QueueMessage { Body = body })));
        }

        private static RedisConsumer Consumer(FakeStream stream) =>
            new(() => stream.Db.Object, Serializer, new RedisStreamSettings { BlockMilliseconds = 10 });

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

        [Fact]
        public async Task A_default_subscription_receives_a_message_published_after_it_started()
        {
            var stream = new FakeStream();
            using var consumer = Consumer(stream);
            var received = new ConcurrentQueue<string>();

            await consumer.SubscribeAsync("orders", (m, _) => { received.Enqueue(m.Body); return Task.CompletedTask; });
            await WaitForAsync(() => !stream.ReadPositions.IsEmpty);
            stream.AddMessage("after-subscribe");

            (await WaitForAsync(() => received.Contains("after-subscribe"))).Should().BeTrue(
                "a subscription without a consumer group must deliver new messages");
            stream.ReadPositions.Should().NotContain("$");
        }

        [Fact]
        public async Task A_default_subscription_skips_what_was_already_in_the_stream()
        {
            var stream = new FakeStream();
            stream.AddMessage("old-1");
            stream.AddMessage("old-2");
            using var consumer = Consumer(stream);
            var received = new ConcurrentQueue<string>();

            await consumer.SubscribeAsync("orders", (m, _) => { received.Enqueue(m.Body); return Task.CompletedTask; });
            await WaitForAsync(() => !stream.ReadPositions.IsEmpty);
            stream.AddMessage("new");

            (await WaitForAsync(() => received.Contains("new"))).Should().BeTrue();
            received.Should().Equal(new[] { "new" }, "FromBeginning is false, so only messages added after the start are delivered");
        }

        [Fact]
        public async Task A_FromBeginning_subscription_reads_the_whole_stream()
        {
            // Contract pin: the "0-0" path already worked.
            var stream = new FakeStream();
            stream.AddMessage("old");
            using var consumer = Consumer(stream);
            var received = new ConcurrentQueue<string>();

            await consumer.SubscribeAsync("orders", (m, _) => { received.Enqueue(m.Body); return Task.CompletedTask; },
                new ConsumerOptions { FromBeginning = true });

            (await WaitForAsync(() => received.Contains("old"))).Should().BeTrue();
        }

        [Fact]
        public async Task An_unparseable_entry_does_not_hold_the_read_position()
        {
            var stream = new FakeStream();
            using var consumer = Consumer(stream);
            var received = new ConcurrentQueue<string>();

            await consumer.SubscribeAsync("orders", (m, _) => { received.Enqueue(m.Body); return Task.CompletedTask; },
                new ConsumerOptions { FromBeginning = true });
            var poison = stream.Add(new NameValueEntry("garbage", "x"));
            await WaitForAsync(() => stream.ReadPositions.Contains(poison));
            await Task.Delay(100);

            // Every read after the poison entry was seen starts past it — never at the position before it.
            var positions = stream.ReadPositions.ToArray();
            var firstPast = Array.IndexOf(positions, poison);
            firstPast.Should().BeGreaterThanOrEqualTo(0, "the position must move past an entry nobody can parse");
            positions.Skip(firstPast).Should().OnlyContain(p => p == poison);
        }
    }
}
