using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Redis;
using Birko.Redis;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Caching.Redis.Tests;

/// <summary>
/// TASK-169 on RedisCache, live: SH-M040 (a stale sliding <c>__meta</c> hash drove a later non-sliding entry's
/// expiry), SH-M041 (pin: a prefix is literal, not a glob), SH-M042 (scan and flush used settings.Database while
/// data went to the connection manager's database) and SH-M043 (one endpoint only; single-node here, so this pins
/// that iterating the primaries changed nothing — the multi-node case is by construction). Gated on
/// <c>BIRKO_REDIS_HOST</c>; skipped without it, failed under <c>BIRKO_REQUIRE_LIVE</c>. Every key is under a
/// per-test GUID prefix, so nothing outside it is touched.
/// </summary>
public class Task169RedisCacheLiveTests(ITestOutputHelper output)
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

    private static string Prefix() => "birko:t169:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task SH_M040_a_non_sliding_write_drops_the_previous_sliding_policy()
    {
        if (Host() is not { } host) return;
        var prefix = Prefix();
        using var manager = new RedisConnectionManager(new RedisSettings(host));
        using var cache = new RedisCache(manager, new RedisSettings(host) { KeyPrefix = prefix });

        await cache.SetAsync("k", "old", CacheEntryOptions.Sliding(TimeSpan.FromMinutes(5)));
        await cache.SetAsync("k", "new", CacheEntryOptions.Absolute(TimeSpan.FromHours(1)));
        (await cache.GetAsync<string>("k")).Value.Should().Be("new");

        var ttl = await manager.GetDatabase().KeyTimeToLiveAsync($"{prefix}:k");
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(30), "the read must not re-expire the entry to the old 5-minute sliding window");
        (await manager.GetDatabase().KeyExistsAsync($"{prefix}:k:__meta")).Should().BeFalse();
        await cache.RemoveAsync("k");
    }

    [Fact]
    public async Task SH_M041_a_prefix_with_glob_characters_removes_only_keys_that_literally_start_with_it()
    {
        if (Host() is not { } host) return;
        using var cache = new RedisCache(new RedisSettings(host) { KeyPrefix = Prefix() });
        await cache.SetAsync("a*b", "literal");
        await cache.SetAsync("aXb", "other");

        await cache.RemoveByPrefixAsync("a*");

        (await cache.GetAsync<string>("a*b")).HasValue.Should().BeFalse();
        (await cache.GetAsync<string>("aXb")).Value.Should().Be("other");
        await cache.ClearAsync();
    }

    [Fact]
    public async Task SH_M042_prefix_removal_and_clear_reach_the_database_the_cache_writes_to()
    {
        if (Host() is not { } host) return;
        // A shared manager on database 0 and cache settings naming database 5: data goes to 0, the manager's.
        using var manager = new RedisConnectionManager(new RedisSettings(host));
        using var cache = new RedisCache(manager, new RedisSettings(host) { Database = 5, KeyPrefix = Prefix() });

        await cache.SetAsync("user:1", "a");
        await cache.SetAsync("user:2", "b");
        await cache.SetAsync("order:1", "c");

        await cache.RemoveByPrefixAsync("user:");
        (await cache.GetAsync<string>("user:1")).HasValue.Should().BeFalse("the scan must run over database 0, where the entry is");
        (await cache.GetAsync<string>("order:1")).Value.Should().Be("c");

        await cache.ClearAsync();
        (await cache.GetAsync<string>("order:1")).HasValue.Should().BeFalse();
    }

    [Fact]
    public void SH_M043_the_manager_lists_every_primary()
    {
        if (Host() is not { } host) return;
        using var manager = new RedisConnectionManager(new RedisSettings(host));

        var servers = manager.GetServers();
        servers.Should().NotBeEmpty();
        servers.Should().OnlyContain(s => !s.IsReplica);
        servers.Select(s => s.EndPoint.ToString()).Should().Contain(manager.GetServer().EndPoint.ToString());
    }
}
