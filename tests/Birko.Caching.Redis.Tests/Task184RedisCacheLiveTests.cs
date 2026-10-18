using System;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Redis;
using Birko.Redis;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Caching.Redis.Tests;

/// <summary>
/// TASK-184 on RedisCache, live: SH-L043 (a disposed cache kept working, and a racing Dispose could dispose an owned
/// connection twice), SH-L045 (a null value threw, where MemoryCache stores it as a hit; decided by the user: store it,
/// a hit) and SH-L048 (the stampede lock had no owner token, so a slow holder deleted another caller's lock). Gated on
/// <c>BIRKO_REDIS_HOST</c>; skipped without it, failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public class Task184RedisCacheLiveTests(ITestOutputHelper output)
{
    private string? Host()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_REDIS_HOST");
        if (!string.IsNullOrWhiteSpace(host)) return host;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException("BIRKO_REDIS_HOST is not set but BIRKO_REQUIRE_LIVE is.");
        output.WriteLine("SKIPPED: BIRKO_REDIS_HOST not set.");
        return null;
    }

    private static string Prefix() => "birko:t184:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task SH_L048_a_holder_whose_lock_expired_does_not_delete_the_next_holders_lock()
    {
        if (Host() is not { } host) return;
        var prefix = Prefix();
        using var manager = new RedisConnectionManager(new RedisSettings(host));
        using var cache = new RedisCache(manager, new RedisSettings(host) { KeyPrefix = prefix });
        var db = manager.GetDatabase();
        var lockKey = $"{prefix}:__lock:k";

        await cache.GetOrSetAsync("k", async _ =>
        {
            // The factory outlived the lock's expiry and another caller took it.
            await db.KeyDeleteAsync(lockKey);
            await db.StringSetAsync(lockKey, "another-caller");
            return "v";
        });

        ((string?)await db.StringGetAsync(lockKey)).Should().Be("another-caller", "only the lock's own holder may release it");
        await db.KeyDeleteAsync(lockKey);
        await cache.ClearAsync();
    }

    [Fact]
    public async Task SH_L045_a_null_value_is_stored_and_read_back_as_a_hit()
    {
        if (Host() is not { } host) return;
        using var cache = new RedisCache(new RedisSettings(host) { KeyPrefix = Prefix() });

        await cache.SetAsync<string?>("k", null);

        var result = await cache.GetAsync<string?>("k");
        result.HasValue.Should().BeTrue("a stored null is a hit, as on MemoryCache (CR-L036)");
        result.Value.Should().BeNull();
        await cache.ClearAsync();
    }

    [Fact]
    public async Task SH_L043_a_disposed_cache_refuses_every_operation()
    {
        if (Host() is not { } host) return;
        var cache = new RedisCache(new RedisSettings(host) { KeyPrefix = Prefix() });
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
    public async Task SH_L041_a_non_positive_ttl_is_refused_as_on_every_backend()
    {
        if (Host() is not { } host) return;
        using var cache = new RedisCache(new RedisSettings(host) { KeyPrefix = Prefix() });
        var negative = new CacheEntryOptions { SlidingExpiration = TimeSpan.FromSeconds(-1) };

        // HEAD: Redis rejected the expiry with a server error, while MemoryCache silently stored an expired entry.
        await FluentActions.Awaiting(() => cache.SetAsync("k", "v", negative)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await cache.ExistsAsync("k")).Should().BeFalse();
    }
}
