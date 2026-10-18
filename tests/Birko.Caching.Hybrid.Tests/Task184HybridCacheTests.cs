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
/// TASK-184 on HybridCache: SH-L038 (the backfill's L1 options ignored the L1MaxExpiration cap), SH-L043 (a disposed
/// cache kept working), SH-L044 (Dispose disposed stampede locks still held), SH-L049 / SH-L050 (pins: fixed by
/// TASK-169's restructuring) and the race TASK-169 handed over: a read that fetched the old value from L2 backfilled
/// it into L1 over a concurrent SetAsync.
/// </summary>
public class Task184HybridCacheTests
{
    /// <summary>Passes through to <see cref="Inner"/>; an armed gate pauses GetAsync after it has read.</summary>
    private sealed class GatedL2 : ICache
    {
        public readonly MemoryCache Inner = new();
        public TaskCompletionSource? GetReached, GetGate;

        public async Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default)
        {
            var result = await Inner.GetAsync<T>(key, ct);
            var gate = GetGate;
            if (gate != null) { GetGate = null; GetReached!.SetResult(); await gate.Task; }
            return result;
        }

        public Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default) => Inner.SetAsync(key, value, options, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default) => Inner.RemoveAsync(key, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Inner.ExistsAsync(key, ct);
        public Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default) => Inner.GetOrSetAsync(key, factory, options, ct);
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Inner.RemoveByPrefixAsync(prefix, ct);
        public Task ClearAsync(CancellationToken ct = default) => Inner.ClearAsync(ct);
        public void Dispose() => Inner.Dispose();
    }

    /// <summary>An L1 whose writes fail.</summary>
    private sealed class FailingWritesL1 : ICache
    {
        private readonly MemoryCache _inner = new();
        public Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default) => _inner.GetAsync<T>(key, ct);
        public Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default) => throw new InvalidOperationException("L1 write failed");
        public Task RemoveAsync(string key, CancellationToken ct = default) => _inner.RemoveAsync(key, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => _inner.ExistsAsync(key, ct);
        public Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default) => _inner.GetOrSetAsync(key, factory, options, ct);
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => _inner.RemoveByPrefixAsync(prefix, ct);
        public Task ClearAsync(CancellationToken ct = default) => _inner.ClearAsync(ct);
        public void Dispose() => _inner.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TASK_184_a_read_that_took_the_old_value_from_L2_does_not_backfill_it_over_a_concurrent_set(bool writeThrough)
    {
        using var l1 = new MemoryCache();
        var l2 = new GatedL2();
        using var cache = new HybridCache(l1, l2, new HybridCacheOptions { WriteThrough = writeThrough });
        await l2.Inner.SetAsync("k", "old");

        var reached = l2.GetReached = new TaskCompletionSource();
        var gate = l2.GetGate = new TaskCompletionSource();
        var read = cache.GetAsync<string>("k");
        await reached.Task; // the read holds "old" from L2 and has not backfilled yet

        await cache.SetAsync("k", "new");
        gate.SetResult();
        await read;

        var inL1 = await l1.GetAsync<string>("k");
        (inL1.HasValue ? inL1.Value : null).Should().NotBe("old", "the set wrote \"new\"; L1 must not serve the value it replaced");
        (await cache.GetAsync<string>("k")).Value.Should().Be("new");
    }

    [Fact]
    public void SH_L038_the_backfill_options_respect_the_L1_cap()
    {
        using var l1 = new MemoryCache();
        using var l2 = new MemoryCache();
        using var cache = new HybridCache(l1, l2, new HybridCacheOptions
        {
            L1DefaultExpiration = TimeSpan.FromMinutes(10),
            L1MaxExpiration = TimeSpan.FromMinutes(1),
        });

        cache.GetL1Options(null).AbsoluteExpiration.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SH_L043_a_disposed_cache_refuses_every_operation()
    {
        using var l1 = new MemoryCache();
        using var l2 = new MemoryCache();
        var cache = new HybridCache(l1, l2);
        cache.Dispose();
        cache.Dispose(); // idempotent

        await FluentActions.Awaiting(() => cache.GetAsync<string>("k")).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.SetAsync("k", "v")).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.GetOrSetAsync("k", _ => Task.FromResult("v"))).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.RemoveAsync("k")).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.ExistsAsync("k")).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.RemoveByPrefixAsync("k")).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.ClearAsync()).Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task SH_L044_dispose_does_not_break_a_GetOrSet_already_running()
    {
        using var l1 = new MemoryCache();
        using var l2 = new MemoryCache();
        var cache = new HybridCache(l1, l2);
        var inFactory = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var running = cache.GetOrSetAsync("k", async _ => { inFactory.SetResult(); await release.Task; return "v"; });
        await inFactory.Task;
        cache.Dispose();
        release.SetResult();

        // The factory's own SetAsync now meets the disposed cache; what must not happen is the semaphore's
        // ObjectDisposedException from the finally replacing whatever the call produced.
        var outcome = await FluentActions.Awaiting(() => running).Should().ThrowAsync<ObjectDisposedException>();
        outcome.Which.ObjectName.Should().Contain(nameof(HybridCache));
    }

    [Fact]
    public async Task SH_L049_a_failing_L2_removal_still_clears_L1_and_propagates_without_fallback()
    {
        using var l1 = new MemoryCache();
        await l1.SetAsync("k", "v");
        using var cache = new HybridCache(l1, new FailingCache(), new HybridCacheOptions { FallbackToL1OnL2Failure = false });

        await FluentActions.Awaiting(() => cache.RemoveAsync("k")).Should().ThrowAsync<Exception>();
        (await l1.GetAsync<string>("k")).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task SH_L050_an_L1_backfill_failure_is_not_taken_for_an_L2_failure()
    {
        var l2 = new MemoryCache();
        await l2.SetAsync("k", "from-l2");
        using var cache = new HybridCache(new FailingWritesL1(), l2, new HybridCacheOptions { FallbackToL1OnL2Failure = true });
        var factoryRan = false;

        await FluentActions.Awaiting(() => cache.GetOrSetAsync("k", _ => { factoryRan = true; return Task.FromResult("factory"); }))
            .Should().ThrowAsync<InvalidOperationException>();
        factoryRan.Should().BeFalse("L2 answered; the factory must not run because L1 failed");
    }

    [Fact]
    public async Task SH_L041_a_non_positive_ttl_is_refused_before_anything_is_written()
    {
        using var l1 = new MemoryCache();
        using var l2 = new MemoryCache();
        using var cache = new HybridCache(l1, l2);
        var zero = new CacheEntryOptions { AbsoluteExpiration = TimeSpan.Zero };

        await FluentActions.Awaiting(() => cache.SetAsync("k", "v", zero)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => cache.GetOrSetAsync("k", _ => Task.FromResult("v"), zero)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await l2.ExistsAsync("k")).Should().BeFalse();
    }
}
