using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Serialization.Yaml;
using Birko.Serialization.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.Serialization.Tests.Yaml
{
    /// <summary>
    /// CR-L365: the four sync-wrapped async methods now observe the CancellationToken up front.
    /// CR-L366: closes coverage gaps — the synchronous Stream overloads, the untyped
    /// DeserializeFromBytes(byte[], Type), the generic SerializeToBytes&lt;T&gt;, and the untyped
    /// SerializeAsync(object) overload were previously unexercised.
    /// </summary>
    public class YamlStreamCoverageTests
    {
        private readonly YamlDotNetSerializer _serializer = new();

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
        public void DeserializeFromBytes_ByType_RoundTrips()
        {
            var payload = new TestPayload { Name = "bytesByType", Value = 12, IsActive = true };
            var bytes = _serializer.SerializeToBytes<TestPayload>(payload);
            var back = _serializer.DeserializeFromBytes(bytes, typeof(TestPayload)) as TestPayload;
            back.Should().NotBeNull();
            back!.Name.Should().Be("bytesByType");
            back.Value.Should().Be(12);
        }

        [Fact]
        public async Task SerializeAsync_ByType_RoundTrips()
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
