using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Redis.Tests
{
    /// <summary>
    /// CR-M206: SendAsync/AcknowledgeAsync/RejectAsync accepted a CancellationToken but never observed it.
    /// They now gate on entry with ThrowIfCancellationRequested (StackExchange.Redis has no per-call token),
    /// so a pre-cancelled token throws before any Redis call — verifiable offline via the lazy connection.
    /// </summary>
    public class RedisCancellationTests
    {
        private static CancellationToken Cancelled()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            return cts.Token;
        }

        [Fact]
        public async Task SendAsync_CancelledToken_Throws()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            await queue.Producer.Invoking(p => p.SendAsync("test", new QueueMessage(), Cancelled()))
                .Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task AcknowledgeAsync_CancelledToken_Throws()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            await queue.Consumer.Invoking(c => c.AcknowledgeAsync(Guid.NewGuid(), Cancelled()))
                .Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task RejectAsync_CancelledToken_Throws()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            await queue.Consumer.Invoking(c => c.RejectAsync(Guid.NewGuid(), requeue: true, Cancelled()))
                .Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
