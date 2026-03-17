using System;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Redis.Tests
{
    public class RedisStreamQueueTests
    {
        [Fact]
        public void Constructor_WithNullSettings_ThrowsArgumentNullException()
        {
            var act = () => new RedisStreamQueue(null!);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("settings");
        }

        [Fact]
        public void Constructor_WithNullConnectionManager_ThrowsArgumentNullException()
        {
            var settings = new RedisStreamSettings();

            var act = () => new RedisStreamQueue(null!, settings);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("connectionManager");
        }

        [Fact]
        public void Constructor_WithConnectionManagerAndNullSettings_ThrowsArgumentNullException()
        {
            var settings = new RedisStreamSettings();
            using var manager = new Birko.Redis.RedisConnectionManager(settings);

            var act = () => new RedisStreamQueue(manager, null!);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("settings");
        }

        [Fact]
        public void Constructor_CreatesProducerAndConsumer()
        {
            var settings = new RedisStreamSettings();
            // Note: ConnectionManager is lazy, so this won't attempt to connect
            using var queue = new RedisStreamQueue(settings);

            queue.Producer.Should().NotBeNull();
            queue.Consumer.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithConnectionManager_ExposesConnectionManager()
        {
            var settings = new RedisStreamSettings();
            using var manager = new Birko.Redis.RedisConnectionManager(settings);
            using var queue = new RedisStreamQueue(manager, settings);

            queue.ConnectionManager.Should().BeSameAs(manager);
        }

        [Fact]
        public void Dispose_DisposesProducerAndConsumer()
        {
            var settings = new RedisStreamSettings();
            var queue = new RedisStreamQueue(settings);

            queue.Dispose();

            // After dispose, producer and consumer should throw on use
            var producerAct = () => queue.Producer.SendAsync("test", message: new QueueMessage()).GetAwaiter().GetResult();
            producerAct.Should().Throw<ObjectDisposedException>();
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var settings = new RedisStreamSettings();
            var queue = new RedisStreamQueue(settings);

            queue.Dispose();
            var act = () => queue.Dispose();

            act.Should().NotThrow();
        }

        [Fact]
        public async Task ConnectAsync_ThrowsAfterDispose()
        {
            var settings = new RedisStreamSettings();
            var queue = new RedisStreamQueue(settings);
            queue.Dispose();

            var act = () => queue.ConnectAsync();

            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        [Fact]
        public async Task DisconnectAsync_CompletesSuccessfully()
        {
            var settings = new RedisStreamSettings();
            using var queue = new RedisStreamQueue(settings);

            var act = () => queue.DisconnectAsync();

            await act.Should().NotThrowAsync();
        }
    }
}
