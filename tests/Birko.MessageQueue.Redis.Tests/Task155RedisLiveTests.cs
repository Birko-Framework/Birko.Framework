using System;
using System.Collections.Concurrent;
using System.Linq;
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
    /// TASK-155 on a live Redis: SH-M101 (a busy stream never reclaimed failures), SH-M102 (a new group replayed the
    /// whole stream although FromBeginning was false) and SH-M104 (a repeated message id lost the earlier entry's PEL
    /// id). Gated on <c>BIRKO_REDIS_HOST</c>; failed under <c>BIRKO_REQUIRE_LIVE</c>.
    /// </summary>
    public sealed class Task155RedisLiveTests(ITestOutputHelper output)
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

        private static RedisStreamQueue Queue(string host, string prefix, Action<RedisStreamSettings>? configure = null)
        {
            var settings = new RedisStreamSettings(host) { StreamPrefix = prefix, BlockMilliseconds = 50 };
            configure?.Invoke(settings);
            return new RedisStreamQueue(settings);
        }

        private static async Task<bool> Eventually(Func<bool> condition, int timeoutMs = 8000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (condition()) return true;
                await Task.Delay(20);
            }
            return condition();
        }

        private static string Prefix() => "birko:t155:" + Guid.NewGuid().ToString("N");

        [Fact]
        public async Task SH_M100_a_failing_handler_without_a_group_is_reported_not_silent()
        {
            if (Host() is not { } host) return;
            using var queue = Queue(host, Prefix());
            var consumer = (RedisConsumer)queue.Consumer;
            var reported = new ConcurrentBag<string>();
            consumer.OnHandlerError = (ex, m) => { reported.Add(m.Body + ":" + ex.Message); return Task.CompletedTask; };
            var delivered = new ConcurrentBag<string>();
            using var sub = await consumer.SubscribeAsync("nogroup", (m, _) =>
            {
                if (m.Body == "bad") throw new InvalidOperationException("boom");
                delivered.Add(m.Body);
                return Task.CompletedTask;
            });
            await Task.Delay(300);
            await queue.Producer.SendAsync("nogroup", new QueueMessage { Body = "bad" }, CancellationToken.None);
            await queue.Producer.SendAsync("nogroup", new QueueMessage { Body = "good" }, CancellationToken.None);

            (await Eventually(() => delivered.Contains("good"))).Should().BeTrue();
            reported.Should().ContainSingle().Which.Should().Be("bad:boom");
        }

        [Fact]
        public async Task SH_M101_a_failed_entry_is_redelivered_while_the_stream_stays_busy()
        {
            if (Host() is not { } host) return;
            using var queue = Queue(host, Prefix(), s => { s.ConsumerGroup = "g"; s.PendingRetryMilliseconds = 200; s.ReadCount = 5; });
            var failedOnce = new ConcurrentDictionary<string, int>();
            var delivered = new ConcurrentBag<string>();
            using var sub = await queue.Consumer.SubscribeAsync("busy", (m, _) =>
            {
                if (m.Body == "poison" && failedOnce.AddOrUpdate(m.Body, 1, (_, n) => n + 1) == 1)
                    throw new InvalidOperationException("first delivery fails");
                delivered.Add(m.Body);
                return Task.CompletedTask;
            });

            await Task.Delay(500); // the group is created on the first poll, at the stream's tail (SH-M102)
            await queue.Producer.SendAsync("busy", new QueueMessage { Body = "poison" }, CancellationToken.None);
            // A backlog and a feeder that never pauses, so no poll comes back empty.
            for (var i = 0; i < 500; i++)
                await queue.Producer.SendAsync("busy", new QueueMessage { Body = "backlog" + i }, CancellationToken.None);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var feeder = Task.Run(async () =>
            {
                var i = 0;
                while (!cts.IsCancellationRequested)
                    await queue.Producer.SendAsync("busy", new QueueMessage { Body = "filler" + i++ }, CancellationToken.None);
            });

            var redelivered = await Eventually(() => delivered.Contains("poison"), 5000);
            cts.Cancel();
            await feeder;
            redelivered.Should().BeTrue("the reclaim pass runs even when every poll returns entries");
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, 3)]
        public async Task SH_M102_a_new_group_starts_where_FromBeginning_says(bool fromBeginning, int oldDelivered)
        {
            if (Host() is not { } host) return;
            var prefix = Prefix();
            using var queue = Queue(host, prefix);
            for (var i = 0; i < 3; i++) await queue.Producer.SendAsync("hist", new QueueMessage { Body = "old" + i }, CancellationToken.None);

            var received = new ConcurrentBag<string>();
            using var sub = await queue.Consumer.SubscribeAsync("hist", (m, _) => { received.Add(m.Body); return Task.CompletedTask; },
                new ConsumerOptions { GroupId = "fresh", FromBeginning = fromBeginning });
            await Task.Delay(500); // the group is created on the first poll
            await queue.Producer.SendAsync("hist", new QueueMessage { Body = "new" }, CancellationToken.None);

            (await Eventually(() => received.Contains("new"))).Should().BeTrue();
            await Task.Delay(300);
            received.Count(b => b.StartsWith("old")).Should().Be(oldDelivered);
        }

        [Fact]
        public async Task SH_M104_acknowledging_a_repeated_message_id_acks_every_entry_it_was_delivered_as()
        {
            if (Host() is not { } host) return;
            var prefix = Prefix();
            using var queue = Queue(host, prefix, s => s.ConsumerGroup = "g");
            var id = Guid.NewGuid();
            var seen = 0;
            using var sub = await queue.Consumer.SubscribeAsync("dup", (_, _) => { Interlocked.Increment(ref seen); return Task.CompletedTask; },
                new ConsumerOptions { AckMode = MessageAckMode.ManualAck, FromBeginning = true });
            await queue.Producer.SendAsync("dup", new QueueMessage { Guid = id, Body = "a" }, CancellationToken.None);
            await queue.Producer.SendAsync("dup", new QueueMessage { Guid = id, Body = "b" }, CancellationToken.None);
            (await Eventually(() => Volatile.Read(ref seen) == 2)).Should().BeTrue();

            await queue.Consumer.AcknowledgeAsync(id);

            var mux = await ConnectionMultiplexer.ConnectAsync(host);
            var pending = await mux.GetDatabase().StreamPendingAsync(new RedisStreamSettings { StreamPrefix = prefix }.GetStreamKey("dup"), "g");
            pending.PendingMessageCount.Should().Be(0, "both entries delivered under that message id are acknowledged");
        }
    }
}
