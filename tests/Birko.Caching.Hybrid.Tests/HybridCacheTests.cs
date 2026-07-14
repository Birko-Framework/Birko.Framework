using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Hybrid;
using Birko.Caching.Memory;
using FluentAssertions;
using Xunit;

namespace Birko.Caching.Hybrid.Tests;

public class HybridCacheTests : IDisposable
{
    private readonly MemoryCache _l1 = new();
    private readonly MemoryCache _l2 = new();
    private readonly HybridCache _sut;

    public HybridCacheTests()
    {
        _sut = new HybridCache(_l1, _l2, new HybridCacheOptions
        {
            L1DefaultExpiration = TimeSpan.FromSeconds(30),
            L1MaxExpiration = TimeSpan.FromMinutes(5)
        });
    }

    public void Dispose()
    {
        _sut.Dispose();
        _l1.Dispose();
        _l2.Dispose();
    }

    [Fact]
    public void Constructor_NullL1_Throws()
    {
        var act = () => new HybridCache(null!, _l2);
        act.Should().Throw<ArgumentNullException>().WithParameterName("l1");
    }

    [Fact]
    public void Constructor_NullL2_Throws()
    {
        var act = () => new HybridCache(_l1, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("l2");
    }

    #region CR-L039: GetL1Options matrix

    private static CacheEntryOptions L1Opts(CacheEntryOptions? requested, TimeSpan? l1Max)
    {
        using var l1 = new MemoryCache();
        using var l2 = new MemoryCache();
        using var cache = new HybridCache(l1, l2, new HybridCacheOptions
        {
            L1DefaultExpiration = TimeSpan.FromSeconds(30),
            L1MaxExpiration = l1Max
        });
        return cache.GetL1Options(requested);
    }

    [Fact]
    public void GetL1Options_NullRequested_UsesDefaultAbsolute()
    {
        var o = L1Opts(null, l1Max: TimeSpan.FromMinutes(5));
        o.AbsoluteExpiration.Should().Be(TimeSpan.FromSeconds(30));
        o.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public void GetL1Options_NullMax_PassesRequestedThrough()
    {
        var requested = CacheEntryOptions.AbsoluteAndSliding(TimeSpan.FromHours(1), TimeSpan.FromMinutes(2));
        var o = L1Opts(requested, l1Max: null);
        o.Should().BeSameAs(requested);
    }

    [Fact]
    public void GetL1Options_AbsoluteBelowMax_Kept()
    {
        var o = L1Opts(CacheEntryOptions.Absolute(TimeSpan.FromMinutes(1)), l1Max: TimeSpan.FromMinutes(5));
        o.AbsoluteExpiration.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GetL1Options_AbsoluteAboveMax_CappedToMax()
    {
        var o = L1Opts(CacheEntryOptions.Absolute(TimeSpan.FromMinutes(10)), l1Max: TimeSpan.FromMinutes(5));
        o.AbsoluteExpiration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void GetL1Options_SlidingOnly_AbsoluteFallsBackAndSlidingCapped()
    {
        // sliding below max is kept; absolute (unset) falls back to min(max, default) = 30s.
        var o = L1Opts(CacheEntryOptions.Sliding(TimeSpan.FromMinutes(1)), l1Max: TimeSpan.FromMinutes(5));
        o.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(1));
        o.AbsoluteExpiration.Should().Be(TimeSpan.FromSeconds(30));

        // sliding above max is capped to max.
        var capped = L1Opts(CacheEntryOptions.Sliding(TimeSpan.FromMinutes(10)), l1Max: TimeSpan.FromMinutes(5));
        capped.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(5));
    }

    #endregion

    [Fact]
    public async Task GetAsync_ReturnsFromL1_WhenPresentInL1()
    {
        await _l1.SetAsync("key", "l1-value", CacheEntryOptions.Absolute(TimeSpan.FromMinutes(1)));

        var result = await _sut.GetAsync<string>("key");

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("l1-value");
    }

    [Fact]
    public async Task GetAsync_FallsBackToL2_WhenL1Miss()
    {
        await _l2.SetAsync("key", "l2-value", CacheEntryOptions.Absolute(TimeSpan.FromMinutes(1)));

        var result = await _sut.GetAsync<string>("key");

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("l2-value");
    }

    [Fact]
    public async Task GetAsync_PopulatesL1FromL2Hit()
    {
        await _l2.SetAsync("key", "l2-value", CacheEntryOptions.Absolute(TimeSpan.FromMinutes(1)));

        await _sut.GetAsync<string>("key");

        // L1 should now have the value
        var l1Result = await _l1.GetAsync<string>("key");
        l1Result.HasValue.Should().BeTrue();
        l1Result.Value.Should().Be("l2-value");
    }

    [Fact]
    public async Task GetAsync_ReturnsMiss_WhenBothMiss()
    {
        var result = await _sut.GetAsync<string>("missing");

        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task SetAsync_WritesBothTiers()
    {
        await _sut.SetAsync("key", "value", CacheEntryOptions.Absolute(TimeSpan.FromMinutes(1)));

        var l1Result = await _l1.GetAsync<string>("key");
        var l2Result = await _l2.GetAsync<string>("key");

        l1Result.HasValue.Should().BeTrue();
        l1Result.Value.Should().Be("value");
        l2Result.HasValue.Should().BeTrue();
        l2Result.Value.Should().Be("value");
    }

    [Fact]
    public async Task RemoveAsync_RemovesFromBothTiers()
    {
        await _sut.SetAsync("key", "value");
        await _sut.RemoveAsync("key");

        var l1Result = await _l1.GetAsync<string>("key");
        var l2Result = await _l2.GetAsync<string>("key");

        l1Result.HasValue.Should().BeFalse();
        l2Result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueForL1()
    {
        await _l1.SetAsync("key", "val");
        (await _sut.ExistsAsync("key")).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrueForL2()
    {
        await _l2.SetAsync("key", "val");
        (await _sut.ExistsAsync("key")).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenAbsent()
    {
        (await _sut.ExistsAsync("missing")).Should().BeFalse();
    }

    [Fact]
    public async Task GetOrSetAsync_ReturnsL1Value_WhenInL1()
    {
        await _l1.SetAsync("key", 42, CacheEntryOptions.Absolute(TimeSpan.FromMinutes(1)));

        var value = await _sut.GetOrSetAsync<int>("key", _ => Task.FromResult(99));

        value.Should().Be(42);
    }

    [Fact]
    public async Task GetOrSetAsync_ReturnsL2Value_WhenInL2()
    {
        await _l2.SetAsync("key", 42, CacheEntryOptions.Absolute(TimeSpan.FromMinutes(1)));

        var factoryCalled = false;
        var value = await _sut.GetOrSetAsync<int>("key", _ =>
        {
            factoryCalled = true;
            return Task.FromResult(99);
        });

        value.Should().Be(42);
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrSetAsync_CallsFactory_WhenBothMiss()
    {
        var value = await _sut.GetOrSetAsync<int>("key", _ => Task.FromResult(99));

        value.Should().Be(99);

        // Both tiers should now have the value
        var l1Result = await _l1.GetAsync<int>("key");
        var l2Result = await _l2.GetAsync<int>("key");
        l1Result.HasValue.Should().BeTrue();
        l2Result.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrSetAsync_DoesNotLeakPerKeyLocks()
    {
        // Regression for CR-H013: the per-key stampede locks were never removed, growing the map
        // one SemaphoreSlim per distinct key forever. After the calls complete, the lock map must
        // be empty (every ref-counted lock released back to zero).
        for (var i = 0; i < 500; i++)
            await _sut.GetOrSetAsync<int>($"key-{i}", _ => Task.FromResult(i));

        LockCount(_sut).Should().Be(0, "uncontended per-key locks must be pruned, not accumulated");
    }

    [Fact]
    public async Task GetOrSetAsync_ConcurrentSameKey_PrunesLockAndCallsFactoryOnce()
    {
        var factoryCalls = 0;
        var tasks = Enumerable.Range(0, 20).Select(_ => _sut.GetOrSetAsync<int>("hot", _ =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult(7);
        }));

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(v => v == 7);
        factoryCalls.Should().Be(1, "the per-key lock must serialize the stampede");
        LockCount(_sut).Should().Be(0, "the shared lock is removed once all waiters release it");
    }

    private static int LockCount(HybridCache cache)
    {
        var field = typeof(HybridCache).GetField("_locks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var dict = (System.Collections.ICollection)field.GetValue(cache)!;
        return dict.Count;
    }

    [Fact]
    public async Task RemoveByPrefixAsync_RemovesFromBothTiers()
    {
        await _sut.SetAsync("user:1", "a");
        await _sut.SetAsync("user:2", "b");
        await _sut.SetAsync("product:1", "c");

        await _sut.RemoveByPrefixAsync("user:");

        (await _l1.ExistsAsync("user:1")).Should().BeFalse();
        (await _l2.ExistsAsync("user:1")).Should().BeFalse();
        (await _l1.ExistsAsync("product:1")).Should().BeTrue();
    }

    [Fact]
    public async Task ClearAsync_ClearsBothTiers()
    {
        await _sut.SetAsync("a", 1);
        await _sut.SetAsync("b", 2);

        await _sut.ClearAsync();

        (await _l1.ExistsAsync("a")).Should().BeFalse();
        (await _l2.ExistsAsync("a")).Should().BeFalse();
    }
}

public class HybridCacheFallbackTests : IDisposable
{
    private readonly MemoryCache _l1 = new();

    public void Dispose() => _l1.Dispose();

    [Fact]
    public async Task GetAsync_FallsBackToL1_WhenL2Throws()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2, new HybridCacheOptions { FallbackToL1OnL2Failure = true });

        await _l1.SetAsync("key", "local");
        var result = await sut.GetAsync<string>("key");

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("local");
    }

    [Fact]
    public async Task GetAsync_ReturnsMiss_WhenL2ThrowsAndL1Miss()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2, new HybridCacheOptions { FallbackToL1OnL2Failure = true });

        var result = await sut.GetAsync<string>("missing");

        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task SetAsync_StillSetsL1_WhenL2Fails()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2, new HybridCacheOptions { FallbackToL1OnL2Failure = true });

        await sut.SetAsync("key", "value");

        var result = await _l1.GetAsync<string>("key");
        result.HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrSetAsync_CallsFactory_WhenL2Fails()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2, new HybridCacheOptions { FallbackToL1OnL2Failure = true });

        var value = await sut.GetOrSetAsync<int>("key", _ => Task.FromResult(42));

        value.Should().Be(42);
    }

    [Fact]
    public async Task SetAsync_PropagatesL2Exception_WhenFallbackDisabled()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2, new HybridCacheOptions { FallbackToL1OnL2Failure = false });

        var act = () => sut.SetAsync("key", "value");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WriteThrough_L2FailsFallbackDisabled_StillObservesL1_AndThrows()
    {
        // Regression for CR-M031: the write-through branch used to leave l1Task unawaited when L2
        // faulted with fallback disabled — an orphaned fire-and-forget write. L1 must be written
        // (and observed) even though the L2 failure propagates.
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2,
            new HybridCacheOptions { WriteThrough = true, FallbackToL1OnL2Failure = false });

        var act = () => sut.SetAsync("key", "value");
        await act.Should().ThrowAsync<InvalidOperationException>();

        // The L1 write must have completed despite the L2 failure.
        var l1 = await _l1.GetAsync<string>("key");
        l1.HasValue.Should().BeTrue("the L1 write must be observed, not orphaned");
        l1.Value.Should().Be("value");
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenL2Fails()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2, new HybridCacheOptions { FallbackToL1OnL2Failure = true });
        await _l1.SetAsync("key", "v");

        var act = () => sut.RemoveAsync("key");
        await act.Should().NotThrowAsync();
        (await _l1.ExistsAsync("key")).Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenL2FailsAndL1Miss()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2, new HybridCacheOptions { FallbackToL1OnL2Failure = true });

        (await sut.ExistsAsync("missing")).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_DoesNotThrow_WhenL2Fails()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2, new HybridCacheOptions { FallbackToL1OnL2Failure = true });
        await _l1.SetAsync("user:1", "a");

        var act = () => sut.RemoveByPrefixAsync("user:");
        await act.Should().NotThrowAsync();
        (await _l1.ExistsAsync("user:1")).Should().BeFalse();
    }

    [Fact]
    public async Task ClearAsync_DoesNotThrow_WhenL2Fails()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2, new HybridCacheOptions { FallbackToL1OnL2Failure = true });
        await _l1.SetAsync("a", 1);

        var act = () => sut.ClearAsync();
        await act.Should().NotThrowAsync();
        (await _l1.ExistsAsync("a")).Should().BeFalse();
    }
}

public class HybridCacheWriteThroughFalseTests : IDisposable
{
    private readonly MemoryCache _l1 = new();
    private readonly MemoryCache _l2 = new();

    public void Dispose() { _l1.Dispose(); _l2.Dispose(); }

    [Fact]
    public async Task WriteThroughFalse_WritesBothTiers()
    {
        using var sut = new HybridCache(_l1, _l2, new HybridCacheOptions { WriteThrough = false });

        await sut.SetAsync("key", "value");

        (await _l1.GetAsync<string>("key")).Value.Should().Be("value");
        (await _l2.GetAsync<string>("key")).Value.Should().Be("value");
    }

    [Fact]
    public async Task WriteThroughFalse_L2FailsFallbackDisabled_Throws()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2,
            new HybridCacheOptions { WriteThrough = false, FallbackToL1OnL2Failure = false });

        var act = () => sut.SetAsync("key", "value");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task WriteThroughFalse_L2FailsFallbackEnabled_StillWritesL1()
    {
        var failingL2 = new FailingCache();
        using var sut = new HybridCache(_l1, failingL2,
            new HybridCacheOptions { WriteThrough = false, FallbackToL1OnL2Failure = true });

        await sut.SetAsync("key", "value");

        (await _l1.GetAsync<string>("key")).Value.Should().Be("value");
    }
}

public class HybridCacheL1CappingTests
{
    private static CacheEntryOptions InvokeGetL1Options(HybridCache cache, CacheEntryOptions? requested)
    {
        var method = typeof(HybridCache).GetMethod("GetL1Options",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (CacheEntryOptions)method.Invoke(cache, new object?[] { requested })!;
    }

    [Fact]
    public void GetL1Options_CapsAbsoluteExpirationToL1Max()
    {
        using var l1 = new MemoryCache();
        using var l2 = new MemoryCache();
        using var sut = new HybridCache(l1, l2, new HybridCacheOptions { L1MaxExpiration = TimeSpan.FromMinutes(2) });

        var capped = InvokeGetL1Options(sut, CacheEntryOptions.Absolute(TimeSpan.FromHours(1)));

        capped.AbsoluteExpiration.Should().Be(TimeSpan.FromMinutes(2), "an L1 TTL above the cap must be reduced");
    }

    [Fact]
    public void GetL1Options_KeepsRequestedExpiration_WhenBelowCap()
    {
        using var l1 = new MemoryCache();
        using var l2 = new MemoryCache();
        using var sut = new HybridCache(l1, l2, new HybridCacheOptions { L1MaxExpiration = TimeSpan.FromMinutes(10) });

        var kept = InvokeGetL1Options(sut, CacheEntryOptions.Absolute(TimeSpan.FromMinutes(1)));

        kept.AbsoluteExpiration.Should().Be(TimeSpan.FromMinutes(1));
    }
}

public class HybridCacheOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var opts = new HybridCacheOptions();

        opts.L1DefaultExpiration.Should().Be(TimeSpan.FromSeconds(30));
        opts.L1MaxExpiration.Should().Be(TimeSpan.FromMinutes(5));
        opts.WriteThrough.Should().BeTrue();
        opts.FallbackToL1OnL2Failure.Should().BeTrue();
    }
}

/// <summary>
/// A cache that throws on every operation, used to test L2 failure fallback.
/// </summary>
internal sealed class FailingCache : ICache
{
    public Task<CacheResult<T>> GetAsync<T>(string key, CancellationToken ct = default) =>
        throw new InvalidOperationException("L2 unavailable");

    public Task SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default) =>
        throw new InvalidOperationException("L2 unavailable");

    public Task RemoveAsync(string key, CancellationToken ct = default) =>
        throw new InvalidOperationException("L2 unavailable");

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        throw new InvalidOperationException("L2 unavailable");

    public Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CacheEntryOptions? options = null, CancellationToken ct = default) =>
        throw new InvalidOperationException("L2 unavailable");

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) =>
        throw new InvalidOperationException("L2 unavailable");

    public Task ClearAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException("L2 unavailable");

    public void Dispose() { }
}
