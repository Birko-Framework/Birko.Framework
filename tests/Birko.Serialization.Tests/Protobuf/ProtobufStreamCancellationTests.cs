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
    /// CR-M245: ProtobufBinarySerializer.SerializeAsync overloads ignored the CancellationToken (unlike
    /// DeserializeAsync, which honors it). CR-M244: the stream round-trip was untested.
    /// </summary>
    public class ProtobufStreamCancellationTests
    {
        private readonly ProtobufBinarySerializer _serializer = new();

        private static CancellationToken Cancelled()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            return cts.Token;
        }

        [Fact]
        public async Task Stream_RoundTrips_Async()
        {
            var payload = new TestPayload { Name = "p", Value = 3, IsActive = true };
            using var ms = new MemoryStream();
            await _serializer.SerializeAsync(ms, payload);
            ms.Position = 0;
            var back = await _serializer.DeserializeAsync<TestPayload>(ms);
            back!.Name.Should().Be("p");
            back.Value.Should().Be(3);
        }

        [Fact]
        public async Task SerializeAsync_ObservesCancelledToken()
        {
            var payload = new TestPayload { Name = "p", Value = 1, IsActive = true };
            using var ms = new MemoryStream();

            await _serializer.Invoking(s => s.SerializeAsync(ms, payload, Cancelled())).Should().ThrowAsync<OperationCanceledException>();
            await _serializer.Invoking(s => s.SerializeAsync<TestPayload>(ms, payload, Cancelled())).Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
