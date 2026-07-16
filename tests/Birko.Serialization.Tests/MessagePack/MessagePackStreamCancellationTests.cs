using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Serialization.MessagePack;
using Birko.Serialization.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.Serialization.Tests.MessagePack
{
    /// <summary>
    /// CR-L358/L359/L360: the Stream overloads now use MessagePack's native (Type/T, Stream, options)
    /// serialize/deserialize (no intermediate byte[] copy), and the async overloads flow the
    /// CancellationToken through the native async stream work. These paths were previously untested.
    /// </summary>
    public class MessagePackStreamCancellationTests
    {
        private readonly MessagePackBinarySerializer _serializer = new();

        private static CancellationToken Cancelled()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            return cts.Token;
        }

        [Fact]
        public void Stream_RoundTrips_Sync()
        {
            var payload = new TestPayload { Name = "sync", Value = 7, IsActive = true };
            using var ms = new MemoryStream();
            _serializer.Serialize(ms, payload);
            ms.Position = 0;
            var back = _serializer.Deserialize<TestPayload>(ms);
            back.Should().NotBeNull();
            back!.Name.Should().Be("sync");
            back.Value.Should().Be(7);
        }

        [Fact]
        public void Stream_RoundTrips_Sync_ByType()
        {
            var payload = new TestPayload { Name = "typed", Value = 9, IsActive = false };
            using var ms = new MemoryStream();
            _serializer.Serialize(ms, (object)payload);
            ms.Position = 0;
            var back = _serializer.Deserialize(ms, typeof(TestPayload)) as TestPayload;
            back.Should().NotBeNull();
            back!.Name.Should().Be("typed");
            back.Value.Should().Be(9);
        }

        [Fact]
        public async Task Stream_RoundTrips_Async()
        {
            var payload = new TestPayload { Name = "async", Value = 3, IsActive = true };
            using var ms = new MemoryStream();
            await _serializer.SerializeAsync(ms, payload);
            ms.Position = 0;
            var back = await _serializer.DeserializeAsync<TestPayload>(ms);
            back.Should().NotBeNull();
            back!.Name.Should().Be("async");
            back.Value.Should().Be(3);
        }

        [Fact]
        public async Task Stream_RoundTrips_Async_ByType()
        {
            var payload = new TestPayload { Name = "asyncTyped", Value = 4, IsActive = false };
            using var ms = new MemoryStream();
            await _serializer.SerializeAsync(ms, (object)payload);
            ms.Position = 0;
            var back = await _serializer.DeserializeAsync(ms, typeof(TestPayload)) as TestPayload;
            back.Should().NotBeNull();
            back!.Name.Should().Be("asyncTyped");
            back.Value.Should().Be(4);
        }

        [Fact]
        public void Serialize_LeavesStreamOpen()
        {
            var payload = new TestPayload { Name = "open", Value = 1, IsActive = true };
            using var ms = new MemoryStream();
            _serializer.Serialize(ms, payload);
            // MessagePack does not dispose the stream: it remains usable/writable afterwards.
            ms.CanWrite.Should().BeTrue();
            ms.Length.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task SerializeAsync_ObservesCancelledToken()
        {
            var payload = new TestPayload { Name = "p", Value = 1, IsActive = true };
            using var ms = new MemoryStream();

            await _serializer.Invoking(s => s.SerializeAsync(ms, payload, Cancelled())).Should().ThrowAsync<OperationCanceledException>();
            await _serializer.Invoking(s => s.SerializeAsync<TestPayload>(ms, payload, Cancelled())).Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task DeserializeAsync_ObservesCancelledToken()
        {
            var payload = new TestPayload { Name = "p", Value = 1, IsActive = true };
            using var ms = new MemoryStream();
            _serializer.Serialize(ms, payload);
            ms.Position = 0;

            await _serializer.Invoking(s => s.DeserializeAsync<TestPayload>(ms, Cancelled())).Should().ThrowAsync<OperationCanceledException>();
            ms.Position = 0;
            await _serializer.Invoking(s => s.DeserializeAsync(ms, typeof(TestPayload), Cancelled())).Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
