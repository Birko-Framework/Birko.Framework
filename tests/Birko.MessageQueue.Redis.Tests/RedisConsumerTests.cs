using System;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Redis.Tests
{
    public class RedisConsumerTests
    {
        [Fact]
        public async Task SubscribeAsync_WithNullDestination_ThrowsArgumentException()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            var act = () => queue.Consumer.SubscribeAsync(null!, (msg, ct) => Task.CompletedTask);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithParameterName("destination");
        }

        [Fact]
        public async Task SubscribeAsync_WithEmptyDestination_ThrowsArgumentException()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            var act = () => queue.Consumer.SubscribeAsync(string.Empty, (msg, ct) => Task.CompletedTask);

            await act.Should().ThrowAsync<ArgumentException>()
                .WithParameterName("destination");
        }

        [Fact]
        public async Task SubscribeAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            var settings = new RedisStreamSettings();
            var queue = new RedisStreamQueue(settings);
            var consumer = queue.Consumer;
            queue.Dispose();

            var act = () => consumer.SubscribeAsync("test", (msg, ct) => Task.CompletedTask);

            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        [Fact]
        public async Task AcknowledgeAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            var settings = new RedisStreamSettings();
            var queue = new RedisStreamQueue(settings);
            var consumer = queue.Consumer;
            queue.Dispose();

            var act = () => consumer.AcknowledgeAsync(Guid.NewGuid());

            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        [Fact]
        public async Task RejectAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            var settings = new RedisStreamSettings();
            var queue = new RedisStreamQueue(settings);
            var consumer = queue.Consumer;
            queue.Dispose();

            var act = () => consumer.RejectAsync(Guid.NewGuid());

            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        [Fact]
        public async Task AcknowledgeAsync_WithUnknownMessageId_DoesNotThrow()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            var act = () => queue.Consumer.AcknowledgeAsync(Guid.NewGuid());

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task RejectAsync_WithUnknownMessageId_DoesNotThrow()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            var act = () => queue.Consumer.RejectAsync(Guid.NewGuid(), requeue: true);

            await act.Should().NotThrowAsync();
        }
    }
}
