using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue;
using Birko.MessageQueue.Redis;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Birko.MessageQueue.Redis.Tests
{
    /// <summary>
    /// TASK-668 on Redis: an explicit PrefetchCount (refused at TASK-174, SH-L110) is the read batch, measured as the
    /// entries a subscription holds delivered-but-unacknowledged in the stream's pending list; and the redelivery mode
    /// the distributed bus's retry rule relies on. Live tests are gated on <c>BIRKO_REDIS_HOST</c>.
    /// </summary>
    public sealed class Task668RedisTests(ITestOutputHelper output)
    {
        private string? Host()
        {
            var host = Environment.GetEnvironmentVariable("BIRKO_REDIS_HOST");
            if (!string.IsNullOrWhiteSpace(host)) return host;
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REDIS_HOST is not set but BIRKO_REQUIRE_LIVE is.");
            output.WriteLine("SKIPPED: BIRKO_REDIS_HOST not set.");
            return null;
        }

        private static async Task<bool> Eventually(Func<Task<bool>> condition, int timeoutMs = 8000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (await condition()) return true;
                await Task.Delay(25);
            }
            return await condition();
        }

        [Theory]
        [InlineData(3)]
        [InlineData(1)] // the default: RedisStreamSettings.ReadCount (5 here) stays in charge
        public async Task A_prefetch_count_bounds_the_entries_pending_for_the_subscription(int prefetch)
        {
            if (Host() is not { } host) return;
            var settings = new RedisStreamSettings(host)
            {
                StreamPrefix = "birko:t668:" + Guid.NewGuid().ToString("N"),
                ConsumerGroup = "g",
                ReadCount = 5,
                BlockMilliseconds = 50,
                PendingRetryMilliseconds = 0,
            };
            using var queue = new RedisStreamQueue(settings);
            var mux = await ConnectionMultiplexer.ConnectAsync(host);
            var db = mux.GetDatabase();
            var key = settings.GetStreamKey("d");
            for (var i = 0; i < 10; i++)
                await queue.Producer.SendAsync("d", new QueueMessage { Body = i.ToString() }, CancellationToken.None);

            var gate = new TaskCompletionSource();
            var handled = 0;
            using var sub = await queue.Consumer.SubscribeAsync("d", async (_, _) =>
            {
                await gate.Task;
                Interlocked.Increment(ref handled);
            }, new ConsumerOptions { PrefetchCount = prefetch, FromBeginning = true });

            var expected = prefetch == 1 ? settings.ReadCount : prefetch;
            // The poll loop creates the group in the background: until it has, XPENDING answers NOGROUP.
            async Task<long> Pending()
            {
                try { return (await db.StreamPendingAsync(key, "g")).PendingMessageCount; }
                catch (RedisServerException) { return -1; }
            }
            (await Eventually(async () => await Pending() == expected))
                .Should().BeTrue($"one read takes {expected} entries");
            await Task.Delay(300);
            (await Pending()).Should().Be(expected, "the next read waits for this batch");

            gate.SetResult();
            (await Eventually(() => Task.FromResult(Volatile.Read(ref handled) == 10))).Should().BeTrue();
            await db.KeyDeleteAsync(key);
        }

        [Fact]
        public void The_redelivery_mode_follows_the_consumer_group_and_the_reclaim()
        {
            using var grouped = new RedisStreamQueue(new RedisStreamSettings("localhost") { ConsumerGroup = "g" });
            ((IRedeliveryCapability)grouped).GetRedeliveryMode(null).Should().Be(RedeliveryMode.Redelivered);

            using var ungrouped = new RedisStreamQueue(new RedisStreamSettings("localhost"));
            ((IRedeliveryCapability)ungrouped).GetRedeliveryMode(null).Should().Be(RedeliveryMode.None, "without a group nothing is pending");
            ((IRedeliveryCapability)ungrouped).GetRedeliveryMode(new ConsumerOptions { GroupId = "x" }).Should().Be(RedeliveryMode.Redelivered);

            using var noReclaim = new RedisStreamQueue(new RedisStreamSettings("localhost") { ConsumerGroup = "g", PendingRetryMilliseconds = 0 });
            ((IRedeliveryCapability)noReclaim).GetRedeliveryMode(null).Should().Be(RedeliveryMode.None, "nothing reclaims a failed entry");
        }

        [Fact]
        public async Task A_prefetch_below_one_is_refused()
        {
            using var queue = new RedisStreamQueue(new RedisStreamSettings("localhost"));
            var subscribe = () => queue.Consumer.SubscribeAsync("d", (_, _) => Task.CompletedTask, new ConsumerOptions { PrefetchCount = 0 });
            await subscribe.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }
    }
}
