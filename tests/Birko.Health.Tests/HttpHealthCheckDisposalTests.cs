using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Health;
using Birko.Health.Data;
using FluentAssertions;
using Xunit;

namespace Birko.Health.Tests;

/// <summary>
/// CR-M192: the six HTTP-based health checks used to store `httpClient ?? new HttpClient()` in a
/// readonly field and never dispose the internally-created instance (they weren't IDisposable). They
/// now track ownership and dispose only the client they created — a caller-supplied client is left
/// alone.
/// </summary>
public class HttpHealthCheckDisposalTests
{
    private sealed class TrackingHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage());

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    public static IEnumerable<object[]> CallerClientFactories => new[]
    {
        new object[] { (Func<HttpClient, IDisposable>)(c => new ElasticSearchHealthCheck("http://x", c)) },
        new object[] { (Func<HttpClient, IDisposable>)(c => new InfluxDbHealthCheck("http://x", c)) },
        new object[] { (Func<HttpClient, IDisposable>)(c => new RavenDbHealthCheck("http://x", c)) },
        new object[] { (Func<HttpClient, IDisposable>)(c => new VaultHealthCheck("http://x", c)) },
        new object[] { (Func<HttpClient, IDisposable>)(c => new CosmosDbHealthCheck("http://x", c)) },
        new object[] { (Func<HttpClient, IDisposable>)(c => new SseHealthCheck("http://x", c)) },
    };

    [Theory]
    [MemberData(nameof(CallerClientFactories))]
    public void Dispose_CallerSuppliedClient_IsNotDisposed(Func<HttpClient, IDisposable> factory)
    {
        var handler = new TrackingHandler();
        var callerClient = new HttpClient(handler);

        var check = factory(callerClient);
        check.Should().BeAssignableTo<IDisposable>();
        check.Dispose();

        // The caller owns callerClient's lifetime — the check must not have disposed it.
        handler.Disposed.Should().BeFalse();
        callerClient.Dispose();
    }

    [Fact]
    public void Dispose_InternallyCreatedClient_IsDisposed()
    {
        var check = new ElasticSearchHealthCheck("http://x");
        var field = typeof(ElasticSearchHealthCheck).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance);
        var client = (HttpClient)field!.GetValue(check)!;

        check.Dispose();

        // A disposed HttpClient throws ObjectDisposedException on use.
        client.Invoking(c => c.CancelPendingRequests()).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_WithoutClient_IsIdempotent()
    {
        var check = new VaultHealthCheck("http://x");

        check.Invoking(c => { c.Dispose(); c.Dispose(); }).Should().NotThrow();
    }
}
