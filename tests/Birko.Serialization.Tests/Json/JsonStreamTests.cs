using System.IO;
using System.Threading.Tasks;
using Birko.Serialization.Json;
using Birko.Serialization.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.Serialization.Tests.Json
{
    /// <summary>
    /// CR-M244: the JSON serializer's stream overloads (sync + async) were untested, including the
    /// Utf8JsonWriter using-dispose flush path on Serialize&lt;T&gt;(Stream, T).
    /// </summary>
    public class JsonStreamTests
    {
        private readonly SystemJsonSerializer _serializer = new();

        [Fact]
        public void Stream_RoundTrips_Sync()
        {
            var payload = new TestPayload { Name = "x", Value = 7, IsActive = true };
            using var ms = new MemoryStream();
            _serializer.Serialize(ms, payload);
            ms.Position = 0;
            var back = _serializer.Deserialize<TestPayload>(ms);
            back!.Name.Should().Be("x");
            back.Value.Should().Be(7);
            back.IsActive.Should().BeTrue();
        }

        [Fact]
        public async Task Stream_RoundTrips_Async()
        {
            var payload = new TestPayload { Name = "y", Value = 9, IsActive = false };
            using var ms = new MemoryStream();
            await _serializer.SerializeAsync(ms, payload);
            ms.Position = 0;
            var back = await _serializer.DeserializeAsync<TestPayload>(ms);
            back!.Name.Should().Be("y");
            back.Value.Should().Be(9);
        }
    }
}
