using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Serialization.Xml;
using FluentAssertions;
using Xunit;

namespace Birko.Serialization.Tests.Xml
{
    /// <summary>
    /// CR-M244: the eight stream ISerializer methods were untested. CR-M243: the four async XML methods
    /// ignored the CancellationToken (no ThrowIfCancellationRequested), so a pre-cancelled token completed
    /// normally instead of throwing.
    /// </summary>
    public class XmlStreamAndCancellationTests
    {
        private readonly SystemXmlSerializer _serializer = new();

        private static CancellationToken Cancelled()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            return cts.Token;
        }

        [Fact]
        public void Stream_RoundTrips_Sync()
        {
            var payload = new XmlTestPayload { Name = "x", Value = 7, IsActive = true };
            using var ms = new MemoryStream();
            _serializer.Serialize(ms, payload);
            ms.Position = 0;
            var back = _serializer.Deserialize<XmlTestPayload>(ms);
            back!.Name.Should().Be("x");
            back.Value.Should().Be(7);
            back.IsActive.Should().BeTrue();
        }

        [Fact]
        public async Task Stream_RoundTrips_Async()
        {
            var payload = new XmlTestPayload { Name = "y", Value = 9, IsActive = false };
            using var ms = new MemoryStream();
            await _serializer.SerializeAsync(ms, payload);
            ms.Position = 0;
            var back = await _serializer.DeserializeAsync<XmlTestPayload>(ms);
            back!.Name.Should().Be("y");
            back.Value.Should().Be(9);
        }

        [Fact]
        public async Task AsyncMethods_ObserveCancelledToken()
        {
            var payload = new XmlTestPayload { Name = "x", Value = 1, IsActive = true };
            using var ms = new MemoryStream();

            await _serializer.Invoking(s => s.SerializeAsync(ms, payload, Cancelled())).Should().ThrowAsync<OperationCanceledException>();
            await _serializer.Invoking(s => s.SerializeAsync<XmlTestPayload>(ms, payload, Cancelled())).Should().ThrowAsync<OperationCanceledException>();
            await _serializer.Invoking(s => s.DeserializeAsync(ms, typeof(XmlTestPayload), Cancelled())).Should().ThrowAsync<OperationCanceledException>();
            await _serializer.Invoking(s => s.DeserializeAsync<XmlTestPayload>(ms, Cancelled())).Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
