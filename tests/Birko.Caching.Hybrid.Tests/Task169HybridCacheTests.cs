using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Hybrid;
using Birko.Caching.Memory;
using FluentAssertions;
using Xunit;

namespace Birko.Caching.Hybrid.Tests;

/// <summary>
/// TASK-169 on HybridCache: SH-M037 (the untyped fallback filter absorbed the caller's own cancellation, so a
/// cancelled read was a miss and a cancelled Remove or Clear reported success) and SH-M048 (a read that missed
/// the cleared L1 wrote the value still in L2 back into L1 while a removal was running).
/// </summary>
public class Task169HybridCacheTests
{
    /// <summary>An L2 whose every call waits until the caller's token is cancelled.</summary>
    private sealed class HangingCache : ICache
    {
        private static async Task Hang(CancellationToken ct) => await Task.Delay(Timeout.Infinite, ct);
        public async Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default) { await Hang(ct); return CacheResult<T>.Miss(); }
        public Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default) => Hang(ct);
        public Task RemoveAsync(string key, CancellationToken ct = default) => Hang(ct);
        public async Task<bool> ExistsAsync(string key, CancellationToken ct = default) { await Hang(ct); return false; }
        public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default) { await Hang(ct); return default!; }
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Hang(ct);
        public Task ClearAsync(CancellationToken ct = default) => Hang(ct);
        public void Dispose() { }
    }

    /// <summary>An L2 that fails with a cancellation of its own (a driver timeout), not the caller's.</summary>
    private sealed class TimingOutCache : ICache
    {
        private static Task Fail() => Task.FromException(new TaskCanceledException("driver timeout"));
        public async Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default) { await Fail(); return default; }
        public Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default) => Fail();
        public Task RemoveAsync(string key, CancellationToken ct = default) => Fail();
        public async Task<bool> ExistsAsync(string key, CancellationToken ct = default) { await Fail(); return false; }
        public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default) { await Fail(); return default!; }
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Fail();
        public Task ClearAsync(CancellationToken ct = default) => Fail();
        public void Dispose() { }
    }

    /// <summary>
    /// A cache that passes through to <see cref="Inner"/>, pausing an operation at a gate when one is armed:
    /// GetAsync after it has read, SetAsync before it writes.
    /// </summary>
    private sealed class GatedCache : ICache
    {
        public readonly MemoryCache Inner = new();
        public TaskCompletionSource? GetReached, GetGate, SetReached, SetGate;

        public async Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default)
        {
            var result = await Inner.GetAsync<T>(key, ct);
            var gate = GetGate;
            if (gate != null) { GetGate = null; GetReached!.SetResult(); await gate.Task; }
            return result;
        }

        public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
        {
            var gate = SetGate;
            if (gate != null) { SetGate = null; SetReached!.SetResult(); await gate.Task; }
            await Inner.SetAsync(key, value, options, ct);
        }

        public Task RemoveAsync(string key, CancellationToken ct = default) => Inner.RemoveAsync(key, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Inner.ExistsAsync(key, ct);
        public Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default) => Inner.GetOrSetAsync(key, factory, options, ct);
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Inner.RemoveByPrefixAsync(prefix, ct);
        public Task ClearAsync(CancellationToken ct = default) => Inner.ClearAsync(ct);
        public void Dispose() => Inner.Dispose();
    }

    [Theory]
    [InlineData("get")]
    [InlineData("exists")]
    [InlineData("remove")]
    [InlineData("removeByPrefix")]
    [InlineData("clear")]
    [InlineData("set")]
    [InlineData("getOrSet")]
    public async Task SH_M037_the_callers_cancellation_is_not_absorbed_as_an_L2_failure(string operation)
    {
        using var l1 = new MemoryCache();
        using var cache = new HybridCache(l1, new HangingCache(), new HybridCacheOptions { FallbackToL1OnL2Failure = true });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var factoryRan = false;

        Func<Task> act = operation switch
        {
            "get" => () => cache.GetAsync<string>("k", cts.Token),
            "exists" => () => cache.ExistsAsync("k", cts.Token),
            "remove" => () => cache.RemoveAsync("k", cts.Token),
            "removeByPrefix" => () => cache.RemoveByPrefixAsync("k", cts.Token),
            "clear" => () => cache.ClearAsync(cts.Token),
            "set" => () => cache.SetAsync("k", "v", null, cts.Token),
            _ => () => cache.GetOrSetAsync("k", _ => { factoryRan = true; return Task.FromResult("v"); }, null, cts.Token),
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        factoryRan.Should().BeFalse("a cancelled L2 read is not a miss to fill");
    }

    [Fact]
    public async Task SH_M037_an_L2_cancellation_that_is_not_the_callers_is_still_absorbed()
    {
        using var l1 = new MemoryCache();
        using var cache = new HybridCache(l1, new TimingOutCache(), new HybridCacheOptions { FallbackToL1OnL2Failure = true });

        (await cache.GetAsync<string>("k")).HasValue.Should().BeFalse();
        (await cache.ExistsAsync("k")).Should().BeFalse();
        await cache.RemoveAsync("k");
        await cache.SetAsync("k", "v");
        (await l1.GetAsync<string>("k")).Value.Should().Be("v");
    }

    [Fact]
    public async Task SH_M048_a_read_that_took_the_value_from_L2_before_a_removal_does_not_put_it_back_in_L1()
    {
        using var l1 = new MemoryCache();
        var l2 = new GatedCache();
        using var cache = new HybridCache(l1, l2);
        await l2.Inner.SetAsync("k", "stale");

        var reached = l2.GetReached = new TaskCompletionSource();
        var gate = l2.GetGate = new TaskCompletionSource();
        var read = cache.GetAsync<string>("k");
        await reached.Task; // the read has the value from L2 and has not backfilled yet

        await cache.RemoveAsync("k");
        gate.SetResult();
        await read;

        (await l1.GetAsync<string>("k")).HasValue.Should().BeFalse("the key was removed; L1 must not serve it");
        (await cache.GetAsync<string>("k")).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task SH_M048_a_backfill_that_lands_after_the_removals_L1_step_is_withdrawn()
    {
        var l1 = new GatedCache();
        using var l2 = new MemoryCache();
        using var cache = new HybridCache(l1, l2);
        await l2.SetAsync("k", "stale");

        var reached = l1.SetReached = new TaskCompletionSource();
        var gate = l1.SetGate = new TaskCompletionSource();
        var read = cache.GetAsync<string>("k");
        await reached.Task; // the read is about to write the L2 value into L1

        await cache.RemoveAsync("k"); // L2 and L1 removed while the backfill is pending
        gate.SetResult();
        await read;

        (await l1.Inner.GetAsync<string>("k")).HasValue.Should().BeFalse();
        l1.Dispose();
    }

    [Fact]
    public async Task SH_M048_a_prefix_removal_also_stops_a_pending_backfill()
    {
        using var l1 = new MemoryCache();
        var l2 = new GatedCache();
        using var cache = new HybridCache(l1, l2);
        await l2.Inner.SetAsync("user:1", "stale");

        var reached = l2.GetReached = new TaskCompletionSource();
        var gate = l2.GetGate = new TaskCompletionSource();
        var read = cache.GetAsync<string>("user:1");
        await reached.Task;

        await cache.RemoveByPrefixAsync("user:");
        gate.SetResult();
        await read;

        (await l1.GetAsync<string>("user:1")).HasValue.Should().BeFalse();
        l2.Dispose();
    }

    [Fact]
    public async Task SH_M048_a_read_with_no_removal_in_flight_still_backfills_L1()
    {
        using var l1 = new MemoryCache();
        using var l2 = new MemoryCache();
        using var cache = new HybridCache(l1, l2);
        await l2.SetAsync("k", "v");

        (await cache.GetAsync<string>("k")).Value.Should().Be("v");
        (await l1.GetAsync<string>("k")).Value.Should().Be("v");
        (await cache.GetOrSetAsync("k", _ => Task.FromResult("factory"))).Should().Be("v");
    }
}
