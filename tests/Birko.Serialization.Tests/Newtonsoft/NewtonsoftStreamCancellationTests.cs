using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Serialization.Newtonsoft;
using Birko.Serialization.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.Serialization.Tests.Newtonsoft
{
    /// <summary>
    /// CR-L361: SerializeAsync now observes the CancellationToken up front (Newtonsoft has no truly-async
    /// serialize path, so a pre-cancelled token must fail before the full synchronous serialize).
    /// CR-L362: the 8 Stream/async overloads were untested — cover round-trips and leaveOpen here.
    /// </summary>
    public class NewtonsoftStreamCancellationTests
    {
        private readonly NewtonsoftJsonSerializer _serializer = new();

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
            // The Stream overloads open the underlying stream with leaveOpen:true, so a second write succeeds.
            var first = new TestPayload { Name = "one", Value = 1, IsActive = true };
            var second = new TestPayload { Name = "two", Value = 2, IsActive = false };
            using var ms = new MemoryStream();

            _serializer.Serialize(ms, first);
            ms.CanWrite.Should().BeTrue();
            _serializer.Serialize(ms, second); // would throw ObjectDisposedException if the first call closed it
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
