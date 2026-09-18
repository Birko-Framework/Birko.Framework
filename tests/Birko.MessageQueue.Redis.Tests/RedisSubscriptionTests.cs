using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Redis.Tests
{
    public class RedisSubscriptionTests
    {
        [Fact]
        public async Task UnsubscribeAsync_SetsIsActiveToFalse()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            // Subscribe will fail to actually poll (no Redis), but the subscription object is created
            var subscription = await queue.Consumer.SubscribeAsync("test-dest", (msg, ct) => Task.CompletedTask);

            subscription.IsActive.Should().BeTrue();
            subscription.Destination.Should().Be("test-dest");

            await subscription.UnsubscribeAsync();

            subscription.IsActive.Should().BeFalse();
        }

        [Fact]
        public async Task UnsubscribeAsync_CalledTwice_DoesNotThrow()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            var subscription = await queue.Consumer.SubscribeAsync("test", (msg, ct) => Task.CompletedTask);

            await subscription.UnsubscribeAsync();
            var act = () => subscription.UnsubscribeAsync();

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task Dispose_SetsIsActiveToFalse()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            var subscription = await queue.Consumer.SubscribeAsync("test", (msg, ct) => Task.CompletedTask);

            subscription.Dispose();

            subscription.IsActive.Should().BeFalse();
        }
    }
}
