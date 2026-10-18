using System;
using System.Collections.Concurrent;
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
    /// TASK-174 on Redis: SH-L102 (poll faults observable only inside the assembly), SH-L103 (an entry with no created_at
    /// never expired), SH-L109 / SH-L110 (Priority and PrefetchCount silently ignored; decided by the user: refuse until
    /// implemented) and SH-L113 (a redelivery carried no delivery count). Live tests are gated on <c>BIRKO_REDIS_HOST</c>.
    /// </summary>
    public sealed class Task174RedisTests(ITestOutputHelper output)
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

        private static async Task<bool> Eventually(Func<bool> condition, int timeoutMs = 6000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (condition()) return true;
                await Task.Delay(20);
            }
            return condition();
        }

        [Fact]
        public void SH_L102_poll_faults_are_observable_outside_the_assembly()
        {
            typeof(RedisConsumer).GetEvent(nameof(RedisConsumer.PollError))!.AddMethod!.IsPublic.Should().BeTrue();
        }

        [Fact]
        public async Task SH_L109_SH_L110_priority_and_prefetch_are_refused_until_implemented()
        {
            using var queue = new RedisStreamQueue(new RedisStreamSettings("localhost"));
            var send = () => queue.Producer.SendAsync("d", new QueueMessage { Body = "x", Priority = 5 }, CancellationToken.None);
            await send.Should().ThrowAsync<NotSupportedException>();
            var subscribe = () => queue.Consumer.SubscribeAsync("d", (_, _) => Task.CompletedTask, new ConsumerOptions { PrefetchCount = 10 });
            await subscribe.Should().ThrowAsync<NotSupportedException>();
        }

        [Fact]
        public async Task SH_L103_an_old_entry_without_created_at_expires_by_its_stream_id()
        {
            if (Host() is not { } host) return;
            var prefix = "birko:t174:" + Guid.NewGuid().ToString("N");
            var settings = new RedisStreamSettings(host) { StreamPrefix = prefix, BlockMilliseconds = 50 };
            using var queue = new RedisStreamQueue(settings);
            var mux = await ConnectionMultiplexer.ConnectAsync(host);
            var key = settings.GetStreamKey("ttl");
            // A foreign producer's entry, appended long ago (stream id 1000-0 = 1970) with a one-second TTL and no created_at.
            await mux.GetDatabase().StreamAddAsync(key, new[] { new NameValueEntry("body", "old"), new NameValueEntry("ttl_ms", "1000") }, "1000-0");
            await mux.GetDatabase().StreamAddAsync(key, new[] { new NameValueEntry("body", "fresh"), new NameValueEntry("ttl_ms", "60000") });

            var received = new ConcurrentBag<string>();
            using var sub = await queue.Consumer.SubscribeAsync("ttl", (m, _) => { received.Add(m.Body); return Task.CompletedTask; },
                new ConsumerOptions { FromBeginning = true });

            (await Eventually(() => received.Contains("fresh"))).Should().BeTrue();
            received.Should().NotContain("old", "an entry older than its TTL by its stream id is expired");
        }

        [Fact]
        public async Task SH_L113_a_redelivered_entry_carries_its_delivery_count()
        {
            if (Host() is not { } host) return;
            var settings = new RedisStreamSettings(host)
            {
                StreamPrefix = "birko:t174:" + Guid.NewGuid().ToString("N"), BlockMilliseconds = 50, ConsumerGroup = "g", PendingRetryMilliseconds = 200,
            };
            using var queue = new RedisStreamQueue(settings);
            var counts = new ConcurrentQueue<string?>();
            using var sub = await queue.Consumer.SubscribeAsync("dc", (m, _) =>
            {
                counts.Enqueue(m.Headers.Custom.TryGetValue("x-delivery-count", out var c) ? c : null);
                if (counts.Count == 1) throw new InvalidOperationException("first delivery fails");
                return Task.CompletedTask;
            }, new ConsumerOptions { FromBeginning = true });
            await Task.Delay(300);
            await queue.Producer.SendAsync("dc", new QueueMessage { Body = "x" }, CancellationToken.None);

            (await Eventually(() => counts.Count >= 2)).Should().BeTrue();
            counts.ToArray()[1].Should().Be("2", "the reclaimed redelivery says it is the second delivery");
        }
    }
}
