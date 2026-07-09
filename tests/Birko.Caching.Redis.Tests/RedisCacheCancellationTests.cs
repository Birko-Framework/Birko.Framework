using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Redis;
using Birko.Redis;
using FluentAssertions;
using Xunit;

namespace Birko.Caching.Redis.Tests;

/// <summary>
/// Regression for CR-M034: every Redis-calling method accepted a CancellationToken but never
/// observed it. Each now calls ThrowIfCancellationRequested() before touching Redis — and because
/// the connection is lazy, a pre-cancelled token throws without any live Redis (verified offline).
/// </summary>
public class RedisCacheCancellationTests
{
    private static RedisCache NewCache() => new(new RedisSettings("localhost") { KeyPrefix = "test" });

    private static CancellationToken Cancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    [Fact]
    public async Task GetAsync_HonorsCancellation()
    {
        using var cache = NewCache();
        var act = async () => await cache.GetAsync<string>("k", Cancelled());
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SetAsync_HonorsCancellation()
    {
        using var cache = NewCache();
        var act = async () => await cache.SetAsync("k", "v", null, Cancelled());
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RemoveAsync_HonorsCancellation()
    {
        using var cache = NewCache();
        var act = async () => await cache.RemoveAsync("k", Cancelled());
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExistsAsync_HonorsCancellation()
    {
        using var cache = NewCache();
        var act = async () => await cache.ExistsAsync("k", Cancelled());
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_HonorsCancellation()
    {
        using var cache = NewCache();
        var act = async () => await cache.RemoveByPrefixAsync("p", Cancelled());
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ClearAsync_HonorsCancellation()
    {
        using var cache = NewCache();
        var act = async () => await cache.ClearAsync(Cancelled());
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetOrSetAsync_HonorsCancellation()
    {
        using var cache = NewCache();
        var act = async () => await cache.GetOrSetAsync<string>("k", _ => Task.FromResult("v"), null, Cancelled());
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
