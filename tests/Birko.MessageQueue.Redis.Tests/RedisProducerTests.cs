using System;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Redis.Tests
{
    public class RedisProducerTests
    {
        [Fact]
        public async Task SendAsync_WithNullDestination_ThrowsArgumentException()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            var act = () => queue.Producer.SendAsync(null!, message: new QueueMessage());

            await act.Should().ThrowAsync<ArgumentException>()
                .WithParameterName("destination");
        }

        [Fact]
        public async Task SendAsync_WithEmptyDestination_ThrowsArgumentException()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            var act = () => queue.Producer.SendAsync(string.Empty, message: new QueueMessage());

            await act.Should().ThrowAsync<ArgumentException>()
                .WithParameterName("destination");
        }

        [Fact]
        public async Task SendAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            var settings = new RedisStreamSettings();
            var queue = new RedisStreamQueue(settings);
            var producer = queue.Producer;
            queue.Dispose();

            var act = () => producer.SendAsync("test", message: new QueueMessage());

            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        [Fact]
        public async Task SendAsyncTyped_AfterDispose_ThrowsObjectDisposedException()
        {
            var settings = new RedisStreamSettings();
            var queue = new RedisStreamQueue(settings);
            var producer = queue.Producer;
            queue.Dispose();

            var act = () => producer.SendAsync("test", new TestPayload { Value = "test" });

            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        private class TestPayload
        {
            public string Value { get; set; } = string.Empty;
        }
    }
}
