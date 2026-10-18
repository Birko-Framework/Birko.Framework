using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Caching.Memory;
using Birko.Caching.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.Caching.Tests.Memory;

/// <summary>
/// TASK-184 on Birko.Caching: SH-L039 (the sweep kept an expired NeverRemove entry), SH-L041 (a non-positive TTL was a
/// silent no-op write), SH-L042 (expiry removal by key deleted a fresh write), SH-L043 (a disposed cache kept accepting
/// writes), SH-L044 (Dispose disposed a semaphore an in-flight caller still held), SH-L045 (a null value could not be
/// serialized) and SH-L052 (expiry read the wall clock and could not be driven).
/// </summary>
public class Task184MemoryCacheTests
{
    /// <summary>A clock a test advances by hand; timestamps are its own ticks.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = TimeSpan.FromDays(1).Ticks;
        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
    }

    private static ConcurrentDictionary<string, MemoryCacheEntry> Entries(MemoryCache cache) =>
        (ConcurrentDictionary<string, MemoryCacheEntry>)typeof(MemoryCache)
            .GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache)!;

    private static void Sweep(MemoryCache cache) =>
        typeof(MemoryCache).GetMethod("EvictExpired", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(cache, null);

    [Fact]
    public async Task SH_L052_expiry_is_measured_on_the_injected_clock()
    {
        var clock = new ManualClock();
        using var cache = new MemoryCache(TimeSpan.FromHours(1), clock);
        await cache.SetAsync("k", "v", CacheEntryOptions.Absolute(TimeSpan.FromMinutes(5)));

        clock.Advance(TimeSpan.FromMinutes(4));
        (await cache.GetAsync<string>("k")).HasValue.Should().BeTrue();
        clock.Advance(TimeSpan.FromMinutes(2));
        (await cache.GetAsync<string>("k")).HasValue.Should().BeFalse("five minutes passed on the cache's clock");
    }

    [Fact]
    public async Task SH_L052_a_sliding_window_is_renewed_by_a_read_on_the_injected_clock()
    {
        var clock = new ManualClock();
        using var cache = new MemoryCache(TimeSpan.FromHours(1), clock);
        await cache.SetAsync("k", "v", CacheEntryOptions.Sliding(TimeSpan.FromMinutes(5)));

        clock.Advance(TimeSpan.FromMinutes(4));
        (await cache.GetAsync<string>("k")).HasValue.Should().BeTrue();
        clock.Advance(TimeSpan.FromMinutes(4));
        (await cache.GetAsync<string>("k")).HasValue.Should().BeTrue("the read four minutes ago renewed the window");
        clock.Advance(TimeSpan.FromMinutes(6));
        (await cache.GetAsync<string>("k")).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task SH_L039_the_sweep_removes_an_expired_entry_whatever_its_priority()
    {
        var clock = new ManualClock();
        using var cache = new MemoryCache(TimeSpan.FromHours(1), clock);
        await cache.SetAsync("k", "v", new CacheEntryOptions { AbsoluteExpiration = TimeSpan.FromMinutes(1), Priority = CachePriority.NeverRemove });

        clock.Advance(TimeSpan.FromMinutes(2));
        Sweep(cache);

        Entries(cache).Should().NotContainKey("k", "the read path already stops serving it; the sweep kept it as dead memory");
    }

    [Fact]
    public async Task SH_L039_the_sweep_removes_an_expired_NeverRemove_entry_on_the_system_clock()
    {
        // The same claim without the injected clock, so it isolates the priority check from SH-L052.
        using var cache = new MemoryCache(TimeSpan.FromHours(1));
        await cache.SetAsync("k", "v", new CacheEntryOptions { AbsoluteExpiration = TimeSpan.FromMilliseconds(1), Priority = CachePriority.NeverRemove });
        await Task.Delay(50);

        Sweep(cache);

        Entries(cache).Should().NotContainKey("k");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SH_L041_the_factories_refuse_a_non_positive_duration(int seconds)
    {
        var d = TimeSpan.FromSeconds(seconds);
        FluentActions.Invoking(() => CacheEntryOptions.Absolute(d)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => CacheEntryOptions.Sliding(d)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => CacheEntryOptions.AbsoluteAndSliding(d, TimeSpan.FromMinutes(1))).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => CacheEntryOptions.AbsoluteAndSliding(TimeSpan.FromMinutes(1), d)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SH_L041_a_write_with_a_non_positive_expiration_is_refused_not_dropped()
    {
        using var cache = new MemoryCache();
        var zero = new CacheEntryOptions { AbsoluteExpiration = TimeSpan.Zero };
        var negativeSliding = new CacheEntryOptions { SlidingExpiration = TimeSpan.FromSeconds(-1) };

        await FluentActions.Awaiting(() => cache.SetAsync("k", "v", zero)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => cache.SetAsync("k", "v", negativeSliding)).Should().ThrowAsync<ArgumentOutOfRangeException>();

        var factoryRan = false;
        await FluentActions.Awaiting(() => cache.GetOrSetAsync("k", _ => { factoryRan = true; return Task.FromResult("v"); }, zero))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        factoryRan.Should().BeFalse("the options are refused before the factory runs");
    }

    [Fact]
    public void SH_L041_Validate_passes_null_and_positive_options()
    {
        CacheEntryOptions.Validate(null).Should().BeNull();
        var ok = new CacheEntryOptions { AbsoluteExpiration = TimeSpan.FromSeconds(1), SlidingExpiration = TimeSpan.FromSeconds(1) };
        CacheEntryOptions.Validate(ok).Should().BeSameAs(ok);
    }

    [Fact]
    public async Task SH_L042_removing_an_expired_entry_keeps_a_value_written_after_it_was_observed()
    {
        var clock = new ManualClock();
        using var cache = new MemoryCache(TimeSpan.FromHours(1), clock);
        await cache.SetAsync("k", "old", CacheEntryOptions.Absolute(TimeSpan.FromMinutes(1)));
        var observed = Entries(cache)["k"];
        clock.Advance(TimeSpan.FromMinutes(2));

        // A concurrent SetAsync lands between a reader observing the expired entry and removing it.
        await cache.SetAsync("k", "fresh", CacheEntryOptions.Absolute(TimeSpan.FromHours(1)));
        typeof(MemoryCache).GetMethod("RemoveIfUnchanged", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(cache, new object[] { "k", observed });

        (await cache.GetAsync<string>("k")).Value.Should().Be("fresh");
    }

    [Fact]
    public async Task SH_L042_the_sweep_does_not_remove_a_fresh_write_racing_it()
    {
        // Deterministic version of the race: the sweep's snapshot is taken by enumeration, so make the entry it
        // enumerates expired while the dictionary already holds a fresh one under the key.
        var clock = new ManualClock();
        using var cache = new MemoryCache(TimeSpan.FromHours(1), clock);
        for (var round = 0; round < 200; round++)
        {
            var key = "k" + round;
            await cache.SetAsync(key, "old", CacheEntryOptions.Absolute(TimeSpan.FromMinutes(1)));
        }
        clock.Advance(TimeSpan.FromMinutes(2));

        var writer = Task.Run(async () =>
        {
            for (var round = 0; round < 200; round++)
                await cache.SetAsync("k" + round, "fresh", CacheEntryOptions.Absolute(TimeSpan.FromHours(1)));
        });
        Sweep(cache);
        await writer;

        for (var round = 0; round < 200; round++)
            (await cache.GetAsync<string>("k" + round)).Value.Should().Be("fresh");
    }

    [Fact]
    public async Task SH_L043_a_disposed_cache_refuses_every_operation()
    {
        var cache = new MemoryCache();
        cache.Dispose();

        await FluentActions.Awaiting(() => cache.GetAsync<string>("k")).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.SetAsync("k", "v")).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.GetOrSetAsync("k", _ => Task.FromResult("v"))).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.RemoveAsync("k")).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.ExistsAsync("k")).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.RemoveByPrefixAsync("k")).Should().ThrowAsync<ObjectDisposedException>();
        await FluentActions.Awaiting(() => cache.ClearAsync()).Should().ThrowAsync<ObjectDisposedException>();
        cache.Dispose(); // idempotent
    }

    [Fact]
    public async Task SH_L044_dispose_does_not_dispose_a_semaphore_an_in_flight_caller_holds()
    {
        var cache = new MemoryCache();
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var inFlight = cache.GetOrSetAsync("k", async _ =>
        {
            entered.SetResult();
            await release.Task;
            return "v";
        });
        await entered.Task;
        var locks = (IDictionary)typeof(MemoryCache).GetField("_locks", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache)!;
        var keyLock = locks["k"]!;
        var semaphore = (SemaphoreSlim)keyLock.GetType().GetField("Semaphore")!.GetValue(keyLock)!;

        cache.Dispose();

        // The holder's Release must still work: on HEAD it threw ObjectDisposedException from the finally.
        FluentActions.Invoking(() => semaphore.CurrentCount).Should().NotThrow();
        FluentActions.Invoking(() => semaphore.Wait(0)).Should().NotThrow<ObjectDisposedException>();
        release.SetResult();
        // The factory finished after Dispose, so storing its result is refused (SH-L043); what must not happen is the
        // finally's Release replacing that with an ObjectDisposedException about a semaphore.
        var ex = await FluentActions.Awaiting(() => inFlight).Should().ThrowAsync<ObjectDisposedException>();
        ex.Which.ObjectName.Should().Contain(nameof(MemoryCache));
    }

    [Fact]
    public void SH_L045_a_null_value_serializes_to_JSON_null_and_back()
    {
        CacheSerializer.Serialize<string?>(null).Should().Equal("null"u8.ToArray());
        CacheSerializer.Deserialize<string>(CacheSerializer.Serialize<string?>(null)).Should().BeNull();
        CacheSerializer.SerializeToString<object?>(null).Should().Be("null");
        CacheSerializer.DeserializeFromString<object>("null").Should().BeNull();
        CacheSerializer.Deserialize<int?>(CacheSerializer.Serialize<int?>(null)).Should().BeNull();
        CacheSerializer.Deserialize<string>(CacheSerializer.Serialize("x")).Should().Be("x", "a non-null value is unchanged");
    }
}
