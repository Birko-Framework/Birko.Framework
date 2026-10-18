using System;
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
    /// TASK-155 over a mocked IDatabase: SH-M103 (a Redis error during subscription setup killed the subscription for
    /// good) and SH-M113 (a message with Delay was published at once).
    /// </summary>
    public class Task155RedisConsumerTests
    {
        private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 6000)
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
        public async Task SH_M103_a_Redis_error_while_creating_the_group_is_retried_not_fatal()
        {
            var db = new Mock<IDatabase>();
            var attempts = 0;
            db.Setup(d => d.StreamCreateConsumerGroupAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue?>(),
                    It.IsAny<bool>(), It.IsAny<CommandFlags>()))
                .Returns(() => Interlocked.Increment(ref attempts) == 1
                    ? Task.FromException<bool>(new RedisConnectionException(ConnectionFailureType.SocketFailure, "blip"))
                    : Task.FromResult(true));
            var served = 0;
            db.Setup(d => d.StreamReadGroupAsync(
                    It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                    It.IsAny<RedisValue?>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<TimeSpan?>(),
                    It.IsAny<CommandFlags>()))
                .Returns(() => Task.FromResult(Interlocked.Increment(ref served) == 1
                    ? new[] { new StreamEntry("1-0", new[] { new NameValueEntry("body", "hi"), new NameValueEntry("id", Guid.NewGuid().ToString()) }) }
                    : Array.Empty<StreamEntry>()));

            using var consumer = new RedisConsumer(() => db.Object, new JsonMessageSerializer(),
                new RedisStreamSettings { ConsumerGroup = "g", BlockMilliseconds = 20 });
            var received = 0;
            var sub = await consumer.SubscribeAsync("dest", (_, _) => { Interlocked.Increment(ref received); return Task.CompletedTask; });

            (await WaitForAsync(() => Volatile.Read(ref received) == 1)).Should().BeTrue("the setup is retried after the blip");
            sub.IsActive.Should().BeTrue();
            attempts.Should().BeGreaterThan(1);
        }

        [Fact]
        public async Task SH_M113_a_delayed_message_is_refused_rather_than_sent_at_once()
        {
            var db = new Mock<IDatabase>();
            using var queue = new RedisStreamQueue(new RedisStreamSettings("localhost"));
            var send = () => queue.Producer.SendAsync("d", new QueueMessage { Body = "x", Delay = TimeSpan.FromMinutes(1) }, CancellationToken.None);
            await send.Should().ThrowAsync<NotSupportedException>();
        }
    }
}
