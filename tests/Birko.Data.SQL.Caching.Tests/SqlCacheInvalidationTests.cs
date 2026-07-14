using Birko.Caching;
using Birko.Caching.Memory;
using Birko.Data.SQL.Caching;
using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.SQL.Caching.Tests;

/// <summary>
/// Tests for the SQL query-cache key/invalidation logic. These underpin the CR-C16 fix: the cached
/// store invalidates by table prefix after a write, so it is essential that every per-query key for a
/// table begins with that table's prefix (otherwise a filter Update/Delete would leave stale entries).
/// The end-to-end "filter write clears the cache" path additionally needs a live SQL backend + model
/// mapping and is covered by the store overrides (compile-verified here); this project closes the
/// CR-H085 "no tests" gap for the pure cache logic + the invalidation mechanism.
/// </summary>
public class SqlCacheInvalidationTests
{
    [Fact]
    public void BuildKey_AlwaysStartsWithTablePrefix()
    {
        var prefix = SqlCacheKeyBuilder.GetTablePrefix("Users");

        SqlCacheKeyBuilder.BuildKey("Users", "x => x.Age > 5", "Name:asc", 10, 0).Should().StartWith(prefix);
        SqlCacheKeyBuilder.BuildKey("Users", null, null, null, null).Should().StartWith(prefix);
    }

    // CR-L178: the filter/order hash segments are the first 8 SHA-256 bytes = exactly 16 hex chars
    // (the comment previously mis-stated "12 bytes"). Lock the segment width.
    [Fact]
    public void BuildKey_HashSegments_Are16HexChars()
    {
        var key = SqlCacheKeyBuilder.BuildKey("Users", "x => x.Age > 5", "Name:asc", null, null);

        // sql:Users:<filterHash>:<orderHash>:_:_
        var parts = key.Split(':');
        parts[2].Should().HaveLength(16).And.MatchRegex("^[0-9a-f]{16}$");
        parts[3].Should().HaveLength(16).And.MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public void BuildKey_IsDeterministic_AndFilterSensitive()
    {
        var a = SqlCacheKeyBuilder.BuildKey("Users", "x => x.Age > 5", null, null, null);
        var b = SqlCacheKeyBuilder.BuildKey("Users", "x => x.Age > 5", null, null, null);
        var c = SqlCacheKeyBuilder.BuildKey("Users", "x => x.Age > 6", null, null, null);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void GetTablePrefix_IsTableScoped()
    {
        SqlCacheKeyBuilder.GetTablePrefix("Users")
            .Should().NotBe(SqlCacheKeyBuilder.GetTablePrefix("Orders"));
    }

    // Verifies the invalidation MECHANISM the cached store invokes: removing by a table's prefix drops
    // exactly that table's cached queries and leaves other tables intact.
    [Fact]
    public async Task RemoveByPrefix_InvalidatesOnlyTheTargetTablesQueries()
    {
        using var cache = new MemoryCache();
        var usersKey = SqlCacheKeyBuilder.BuildKey("Users", "x => x.Age > 5", null, null, null);
        var usersKey2 = SqlCacheKeyBuilder.BuildKey("Users", null, null, 10, 0);
        var ordersKey = SqlCacheKeyBuilder.BuildKey("Orders", null, null, null, null);

        var opts = CacheEntryOptions.Absolute(TimeSpan.FromMinutes(5));
        await cache.SetAsync(usersKey, "u1", opts);
        await cache.SetAsync(usersKey2, "u2", opts);
        await cache.SetAsync(ordersKey, "o1", opts);

        await cache.RemoveByPrefixAsync(SqlCacheKeyBuilder.GetTablePrefix("Users"));

        (await cache.GetAsync<string>(usersKey)).HasValue.Should().BeFalse();
        (await cache.GetAsync<string>(usersKey2)).HasValue.Should().BeFalse();
        (await cache.GetAsync<string>(ordersKey)).HasValue.Should().BeTrue();
    }
}
