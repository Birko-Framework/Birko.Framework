using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Birko.MessageQueue.Redis.Tests
{
    public class RedisProducerTests
    {
        private static (Mock<IDatabase> db, Func<NameValueEntry[]?> captured) MockDb()
        {
            var db = new Mock<IDatabase>();
            NameValueEntry[]? captured = null;
            db.Setup(d => d.StreamAddAsync(
                    It.IsAny<RedisKey>(), It.IsAny<NameValueEntry[]>(), It.IsAny<RedisValue?>(),
                    It.IsAny<long?>(), It.IsAny<bool>(), It.IsAny<long?>(),
                    It.IsAny<StreamTrimMode>(), It.IsAny<CommandFlags>()))
                .Callback<RedisKey, NameValueEntry[], RedisValue?, long?, bool, long?, StreamTrimMode, CommandFlags>(
                    (k, e, id, ml, approx, limit, trim, flags) => captured = e)
                .ReturnsAsync((RedisValue)"1-0");
            return (db, () => captured);
        }

        [Fact]
        public async Task SendAsync_WritesOnlyMessageField_NoRedundantPerFieldEntries()
        {
            // CR-L293: the XADD writes only the full 'message' blob, not the duplicated per-field entries.
            var (db, captured) = MockDb();
            var producer = new RedisProducer(() => db.Object, new JsonMessageSerializer(), new RedisStreamSettings());

            await producer.SendAsync("dest", message: new QueueMessage { Body = "hi", PayloadType = "T", Priority = 3 });

            captured()!.Select(e => e.Name.ToString()).Should().BeEquivalentTo(new[] { "message" });
        }

        [Fact]
        public async Task SendAsync_WithTtl_WritesMessageAndTtlOnly()
        {
            var (db, captured) = MockDb();
            var producer = new RedisProducer(() => db.Object, new JsonMessageSerializer(), new RedisStreamSettings());

            await producer.SendAsync("dest", message: new QueueMessage { Body = "hi", TimeToLive = TimeSpan.FromSeconds(30) });

            captured()!.Select(e => e.Name.ToString()).Should().BeEquivalentTo(new[] { "message", "ttl_ms" });
        }

        [Fact]
        public async Task SendAsync_MessageBlob_RoundTripsAllFields()
        {
            // CR-L293: the single 'message' blob still carries every field the per-field entries used to.
            var (db, captured) = MockDb();
            var serializer = new JsonMessageSerializer();
            var producer = new RedisProducer(() => db.Object, serializer, new RedisStreamSettings());

            await producer.SendAsync("dest", message: new QueueMessage { Body = "hi", PayloadType = "T", Priority = 7 });

            var messageField = captured()!.First(e => e.Name == "message").Value.ToString();
            var parsed = serializer.Deserialize<QueueMessage>(messageField);
            parsed.Should().NotBeNull();
            parsed!.Body.Should().Be("hi");
            parsed.PayloadType.Should().Be("T");
            parsed.Priority.Should().Be(7);
        }

        [Fact]
        public async Task SendAsyncTyped_StampsSerializerContentType()
        {
            // CR-L294: the typed send stamps the serializer's content type even when the caller passes headers.
            var (db, captured) = MockDb();
            var serializer = new JsonMessageSerializer();
            var producer = new RedisProducer(() => db.Object, serializer, new RedisStreamSettings());

            await producer.SendAsync("dest", new TestPayload { Value = "x" },
                new MessageHeaders { ContentType = "text/plain" });

            var messageField = captured()!.First(e => e.Name == "message").Value.ToString();
            var parsed = serializer.Deserialize<QueueMessage>(messageField);
            parsed!.Headers.ContentType.Should().Be(serializer.ContentType);
        }

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
