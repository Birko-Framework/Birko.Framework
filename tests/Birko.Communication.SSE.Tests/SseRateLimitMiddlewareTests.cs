using System;
using System.Threading.Tasks;
using Birko.Communication.SSE;
using Birko.Communication.SSE.Middleware;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Birko.Communication.SSE.Tests;

/// <summary>
/// CR-H035 coverage for the rate-limit middleware, and CR-H034: the middleware now implements
/// IDisposable (its lock semaphore was previously leaked) and uses the BCL SemaphoreSlim.
/// </summary>
public class SseRateLimitMiddlewareTests
{
    private static SseRateLimitMiddleware New(int max) =>
        new(NullLogger<SseRateLimitMiddleware>.Instance, maxConnectionsPerMinute: max);

    private static SseContext Context(string endpoint) => new() { RemoteEndPoint = endpoint };

    [Fact]
    public async Task AllowsUpToLimitThenDenies()
    {
        using var mw = New(max: 2);
        var ctx = Context("1.2.3.4");
        SseRequestDelegate next = _ => Task.FromResult<SseResponse?>(new SseResponse { StatusCode = 200 });

        (await mw.ProcessAsync(ctx, next))!.StatusCode.Should().Be(200);
        (await mw.ProcessAsync(ctx, next))!.StatusCode.Should().Be(200);
        (await mw.ProcessAsync(ctx, next))!.StatusCode.Should().Be(429, "the third attempt exceeds the limit");
    }

    [Fact]
    public async Task LimitIsPerClientEndpoint()
    {
        using var mw = New(max: 1);
        SseRequestDelegate next = _ => Task.FromResult<SseResponse?>(new SseResponse { StatusCode = 200 });

        (await mw.ProcessAsync(Context("a"), next))!.StatusCode.Should().Be(200);
        (await mw.ProcessAsync(Context("b"), next))!.StatusCode.Should().Be(200, "a different client has its own budget");
        (await mw.ProcessAsync(Context("a"), next))!.StatusCode.Should().Be(429);
    }

    [Fact]
    public void IsDisposable_AndDisposeDoesNotThrow()
    {
        typeof(SseRateLimitMiddleware).Should().BeAssignableTo<IDisposable>();

        var mw = New(max: 5);
        var act = () => mw.Dispose();
        act.Should().NotThrow();
    }
}
