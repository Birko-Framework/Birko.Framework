using System;
using Birko.Caching.Redis;
using FluentAssertions;
using Xunit;

namespace Birko.Caching.Redis.Tests;

/// <summary>
/// Regression for CR-H014: sliding+absolute entries stored the absolute *window* and recomputed
/// min(sliding, window) on every hit, so the cap never shrank and an always-accessed entry lived
/// forever. The TTL is now computed against a fixed absolute *deadline*, exercised here via the
/// pure ComputeRefreshedTtl helper (no live Redis needed).
/// </summary>
public class RedisCacheSlidingExpirationTests
{
    [Fact]
    public void NoAbsoluteCap_UsesFullSlidingSpan()
    {
        var ttl = RedisCache.ComputeRefreshedTtl(slidingSeconds: 60, absoluteDeadlineUnix: -1, nowUnix: 1_000);

        ttl.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void DeadlineFartherThanSliding_UsesSliding()
    {
        // now=1000, deadline=2000 (1000s away) > sliding 60 => sliding wins.
        var ttl = RedisCache.ComputeRefreshedTtl(60, 2_000, 1_000);

        ttl.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void DeadlineNearerThanSliding_CapsAtRemaining()
    {
        // now=1000, deadline=1030 (30s away) < sliding 60 => capped to 30.
        var ttl = RedisCache.ComputeRefreshedTtl(60, 1_030, 1_000);

        ttl.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void RemainingBudgetShrinksAsTimeAdvances()
    {
        // The core CR-H014 property: with a fixed deadline the cap strictly decreases over time,
        // so a repeatedly-accessed entry cannot be re-extended forever.
        const long deadline = 1_100;
        var early = RedisCache.ComputeRefreshedTtl(1000, deadline, 1_000); // 100s remaining
        var later = RedisCache.ComputeRefreshedTtl(1000, deadline, 1_050); // 50s remaining

        early.Should().Be(TimeSpan.FromSeconds(100));
        later.Should().Be(TimeSpan.FromSeconds(50));
        later!.Value.Should().BeLessThan(early!.Value);
    }

    [Fact]
    public void PastDeadline_ReturnsNull_SignallingExpiry()
    {
        var ttl = RedisCache.ComputeRefreshedTtl(60, 1_000, 1_001); // 1s past deadline

        ttl.Should().BeNull();
    }
}
