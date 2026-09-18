using Birko.AI.Models;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Tests
{
    public class LlmStreamingResponseTests
    {
        private sealed class TrackingDisposable : IDisposable
        {
            public int DisposeCount { get; private set; }
            public void Dispose() => DisposeCount++;
        }

        [Fact]
        public void Dispose_ReleasesUnderlyingResource()
        {
            // Regression for CR-M003: an abandoned streaming response must release the underlying
            // transport (HTTP response) instead of leaking the connection.
            var resource = new TrackingDisposable();
            var response = new LlmStreamingResponse
            {
                GetStreamAsync = () => Task.FromResult<IAsyncEnumerable<string>>(Empty()),
                Resource = resource
            };

            response.Dispose();

            resource.DisposeCount.Should().Be(1);
            response.Resource.Should().BeNull();
        }

        [Fact]
        public async Task DisposeAsync_ReleasesUnderlyingResource()
        {
            var resource = new TrackingDisposable();
            var response = new LlmStreamingResponse
            {
                GetStreamAsync = () => Task.FromResult<IAsyncEnumerable<string>>(Empty()),
                Resource = resource
            };

            await response.DisposeAsync();

            resource.DisposeCount.Should().Be(1);
        }

        [Fact]
        public void Dispose_WithNoResource_DoesNotThrow()
        {
            var response = new LlmStreamingResponse
            {
                GetStreamAsync = () => Task.FromResult<IAsyncEnumerable<string>>(Empty())
            };

            var act = () => response.Dispose();
            act.Should().NotThrow();
        }

        private static async IAsyncEnumerable<string> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
