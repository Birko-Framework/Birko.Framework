using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Communication.SSE.Middleware;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Birko.Communication.SSE.Tests;

/// <summary>
/// CR-M067: the rate-limit middleware now prunes every client's history each request and DROPS
/// keys that have fully aged out, so _connectionHistory can't grow without bound. The window is
/// hard-coded to 1 minute and isn't ctor-configurable, so it is shrunk to a few ms via reflection
/// to age an entry out with a minimal wait; _connectionHistory (private) is reflected to assert
/// the stale key was removed.
/// </summary>
public class SseRateLimitEvictionTests
{
    private static SseRateLimitMiddleware New(int max) =>
        new(NullLogger<SseRateLimitMiddleware>.Instance, maxConnectionsPerMinute: max);

    private static SseContext Context(string endpoint) => new() { RemoteEndPoint = endpoint };

    private static void SetWindow(SseRateLimitMiddleware mw, TimeSpan window)
    {
        var field = typeof(SseRateLimitMiddleware).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        field!.SetValue(mw, window);
    }

    private static Dictionary<string, List<DateTime>> History(SseRateLimitMiddleware mw)
    {
        var field = typeof(SseRateLimitMiddleware).GetField("_connectionHistory", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        return (Dictionary<string, List<DateTime>>)field!.GetValue(mw)!;
    }

    [Fact]
    public async Task ProcessAsync_EvictsFullyAgedOutClientKeys()
    {
        using var mw = New(max: 60);
        SetWindow(mw, TimeSpan.FromMilliseconds(50));
        SseRequestDelegate next = _ => Task.FromResult<SseResponse?>(new SseResponse { StatusCode = 200 });

        // Record a connection for client A.
        await mw.ProcessAsync(Context("client-A"), next);
        History(mw).Should().ContainKey("client-A");

        // Wait past the (tiny) window so A's only timestamp ages out.
        await Task.Delay(120);

        // A request for a different client must prune A's stale, empty key entirely.
        await mw.ProcessAsync(Context("client-B"), next);

        var history = History(mw);
        history.Should().ContainKey("client-B");
        history.Should().NotContainKey("client-A", "A's only timestamp aged out, so its key must be removed (CR-M067)");
    }
}
