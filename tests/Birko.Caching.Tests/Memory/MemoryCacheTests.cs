using Birko.Caching;
using Birko.Caching.Memory;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Caching.Tests.Memory;

public class MemoryCacheTests
{
    #region GetAsync / SetAsync

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsMiss()
    {
        using var cache = new MemoryCache();

        var result = await cache.GetAsync<string>("missing");

        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsHit()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync("key", "value");

        var result = await cache.GetAsync<string>("key");

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("value");
    }

    [Fact]
    public async Task SetAsync_OverwritesExistingKey()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync("key", "old");
        await cache.SetAsync("key", "new");

        var result = await cache.GetAsync<string>("key");

        result.Value.Should().Be("new");
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task RemoveAsync_RemovesKey()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync("key", "value");

        await cache.RemoveAsync("key");

        var result = await cache.GetAsync<string>("key");
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_NonExistentKey_DoesNotThrow()
    {
        using var cache = new MemoryCache();

        var act = () => cache.RemoveAsync("missing");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region ExistsAsync

    [Fact]
    public async Task ExistsAsync_ExistingKey_ReturnsTrue()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync("key", "value");

        var exists = await cache.ExistsAsync("key");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_MissingKey_ReturnsFalse()
    {
        using var cache = new MemoryCache();

        var exists = await cache.ExistsAsync("missing");

        exists.Should().BeFalse();
    }

    #endregion

    #region Expiration

    [Fact]
    public async Task GetAsync_ExpiredAbsolute_ReturnsMiss()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync("key", "value", CacheEntryOptions.Absolute(TimeSpan.FromMilliseconds(50)));

        await Task.Delay(100);

        var result = await cache.GetAsync<string>("key");
        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_ExpiredEntry_ReturnsFalse()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync("key", "value", CacheEntryOptions.Absolute(TimeSpan.FromMilliseconds(50)));

        await Task.Delay(100);

        var exists = await cache.ExistsAsync("key");
        exists.Should().BeFalse();
    }

    #endregion

    #region RemoveByPrefixAsync

    [Fact]
    public async Task RemoveByPrefixAsync_RemovesMatchingKeys()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync("user:1", "Alice");
        await cache.SetAsync("user:2", "Bob");
        await cache.SetAsync("order:1", "Order1");

        await cache.RemoveByPrefixAsync("user:");

        (await cache.ExistsAsync("user:1")).Should().BeFalse();
        (await cache.ExistsAsync("user:2")).Should().BeFalse();
        (await cache.ExistsAsync("order:1")).Should().BeTrue();
    }

    #endregion

    #region ClearAsync

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync("a", 1);
        await cache.SetAsync("b", 2);

        await cache.ClearAsync();

        (await cache.ExistsAsync("a")).Should().BeFalse();
        (await cache.ExistsAsync("b")).Should().BeFalse();
    }

    #endregion

    #region GetOrSetAsync

    [Fact]
    public async Task GetOrSetAsync_MissingKey_CallsFactory()
    {
        using var cache = new MemoryCache();
        var called = false;

        var value = await cache.GetOrSetAsync("key", async ct =>
        {
            called = true;
            return "computed";
        });

        called.Should().BeTrue();
        value.Should().Be("computed");
    }

    [Fact]
    public async Task GetOrSetAsync_ExistingKey_DoesNotCallFactory()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync("key", "existing");
        var called = false;

        var value = await cache.GetOrSetAsync("key", async ct =>
        {
            called = true;
            return "computed";
        });

        called.Should().BeFalse();
        value.Should().Be("existing");
    }

    [Fact]
    public async Task GetOrSetAsync_ConcurrentSameKey_CallsFactoryOnce()
    {
        // Regression for CR-M030: a lock evicted between GetOrAdd and WaitAsync let two callers run
        // the factory concurrently. The per-key lock is now ref-counted (never timer-evicted), so a
        // stampede on one key runs the factory exactly once.
        using var cache = new MemoryCache(cleanupInterval: TimeSpan.FromMilliseconds(1));
        var factoryCalls = 0;

        var tasks = Enumerable.Range(0, 32).Select(_ => cache.GetOrSetAsync("hot", async ct =>
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Delay(20, ct);
            return "value";
        }));
        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(v => v == "value");
        factoryCalls.Should().Be(1, "the per-key lock must serialize the stampede");
    }

    [Fact]
    public async Task GetOrSetAsync_ReleasesPerKeyLocks()
    {
        using var cache = new MemoryCache();

        for (var i = 0; i < 200; i++)
            await cache.GetOrSetAsync($"key-{i}", ct => Task.FromResult(i));

        LockCount(cache).Should().Be(0, "uncontended per-key locks must be retired by their last releaser");
    }

    private static int LockCount(MemoryCache cache)
    {
        var field = typeof(MemoryCache).GetField("_locks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var dict = (System.Collections.ICollection)field.GetValue(cache)!;
        return dict.Count;
    }

    #endregion

    #region CR-L034 / L036 / L037

    [Fact]
    public async Task GetAsync_TypeMismatch_DegradesToMiss()
    {
        // Regression for CR-L036: reading a key with an incompatible T threw InvalidCastException from
        // the unchecked (T)entry.Value! cast. It must now degrade to a Miss.
        using var cache = new MemoryCache();
        await cache.SetAsync("k", "a string");

        var mismatch = await cache.GetAsync<int>("k");
        var match = await cache.GetAsync<string>("k");

        mismatch.HasValue.Should().BeFalse();
        match.HasValue.Should().BeTrue();
        match.Value.Should().Be("a string");
    }

    [Fact]
    public async Task GetAsync_StoredNull_IsAHit()
    {
        using var cache = new MemoryCache();
        await cache.SetAsync<string?>("k", null);

        var result = await cache.GetAsync<string?>("k");

        result.HasValue.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Theory]
    [InlineData("get")]
    [InlineData("set")]
    [InlineData("remove")]
    [InlineData("exists")]
    [InlineData("removeByPrefix")]
    [InlineData("clear")]
    public async Task AsyncMethods_ObserveCancellationToken(string op)
    {
        // Regression for CR-L034: the async CRUD methods accepted a CancellationToken but never observed
        // it; an already-cancelled token now throws uniformly (GetOrSetAsync already honored it).
        using var cache = new MemoryCache();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ct = cts.Token;

        Func<Task> act = op switch
        {
            "get" => () => cache.GetAsync<string>("k", ct),
            "set" => () => cache.SetAsync("k", "v", null, ct),
            "remove" => () => cache.RemoveAsync("k", ct),
            "exists" => () => cache.ExistsAsync("k", ct),
            "removeByPrefix" => () => cache.RemoveByPrefixAsync("k", ct),
            _ => () => cache.ClearAsync(ct),
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SlidingExpiration_AccessResetsTheWindow()
    {
        // Regression for CR-L037 (sliding-expiration coverage): each access resets the window, so a key
        // touched within the window stays alive past its original creation-relative lifetime.
        using var cache = new MemoryCache();
        await cache.SetAsync("k", "v", CacheEntryOptions.Sliding(TimeSpan.FromMilliseconds(200)));

        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(90);
            (await cache.GetAsync<string>("k")).HasValue.Should().BeTrue("access must reset the sliding window");
        }

        await Task.Delay(350); // no access for longer than the window
        (await cache.GetAsync<string>("k")).HasValue.Should().BeFalse();
    }

    #endregion
}
