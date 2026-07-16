using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Serialization.Protobuf;
using Birko.Serialization.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.Serialization.Tests.Protobuf
{
    /// <summary>
    /// CR-L364: closes the remaining coverage gaps beyond ProtobufStreamCancellationTests (CR-M244/M245) —
    /// the synchronous Stream overloads, the non-generic object serialize/deserialize path (protobuf-net 3.x
    /// is sensitive to the generic-vs-object overload), and the DeserializeAsync cancelled-token gap.
    /// CR-L363: DeserializeAsync no longer offloads to Task.Run; it observes the token up front.
    /// </summary>
    public class ProtobufStreamCoverageTests
    {
        private readonly ProtobufBinarySerializer _serializer = new();

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
        public void SerializeToBytes_NonGenericObject_RoundTrips()
        {
            // Non-generic Serialize(object) boxes the value — protobuf-net 3.x resolves a different overload
            // than the generic path, so exercise it explicitly.
            var payload = new TestPayload { Name = "boxed", Value = 21, IsActive = true };
            var bytes = _serializer.SerializeToBytes((object)payload);
            var back = _serializer.DeserializeFromBytes(bytes, typeof(TestPayload)) as TestPayload;
            back.Should().NotBeNull();
            back!.Name.Should().Be("boxed");
            back.Value.Should().Be(21);
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
