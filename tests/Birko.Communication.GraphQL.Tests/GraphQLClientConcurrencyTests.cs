using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.GraphQL;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.GraphQL.Tests;

/// <summary>
/// Regression for CR-M045: ExecuteAsync wrapped the whole HTTP round-trip in a SemaphoreSlim(1,1),
/// so a shared (process-wide, per-endpoint) client funneled every caller into strictly sequential
/// requests. HttpClient is thread-safe; requests must be able to run concurrently now.
/// </summary>
public class GraphQLClientConcurrencyTests
{
    private sealed class GatingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _release = new();
        private int _concurrent;
        public TaskCompletionSource TwoInFlight { get; } = new();

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _concurrent) >= 2)
                TwoInFlight.TrySetResult();

            await _release.Task.WaitAsync(ct); // hold the request open until the test releases it
            Interlocked.Decrement(ref _concurrent);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{}}""")
            };
        }
    }

    [Fact]
    public async Task ExecuteAsync_AllowsConcurrentRequests_OnSharedClient()
    {
        var handler = new GatingHandler();
        using var client = new GraphQLClient(
            new GraphQLSettings { Endpoint = "https://api.example.com/graphql" },
            new HttpClient(handler));

        // Fire two requests on the same client without releasing the handler.
        var t1 = client.QueryAsync<object>("{ a }");
        var t2 = client.QueryAsync<object>("{ b }");

        // With the old _requestLock the second request could never reach the handler while the first
        // was in flight, so "two in flight" would never signal and this would time out.
        var winner = await Task.WhenAny(handler.TwoInFlight.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        winner.Should().Be(handler.TwoInFlight.Task, "requests on a shared client must not be serialized");

        handler.Release();
        await Task.WhenAll(t1, t2);
    }
}
