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
    // SH-H005: cache keys are now scoped by database identity (Settings.GetId()), so both key builders
    // take that scope as their first argument. These tests supply a fixed one — they are about the key's
    // SHAPE and its sensitivity to the other components, which is unchanged. The scope's own behaviour
    // (two databases no longer colliding, invalidation staying aligned with lookup) is covered in
    // Birko.Data.SQL.Caching.Tests/CrossDatabaseAndFilterKeyingTests.
    private const string Scope = "localhost:appdb";

    [Fact]
    public void BuildKey_AlwaysStartsWithTablePrefix()
    {
        var prefix = SqlCacheKeyBuilder.GetTablePrefix(Scope, "Users");

        SqlCacheKeyBuilder.BuildKey(Scope, "Users", "x => x.Age > 5", "Name:asc", 10, 0).Should().StartWith(prefix);
        SqlCacheKeyBuilder.BuildKey(Scope, "Users", null, null, null, null).Should().StartWith(prefix);
    }

    // CR-L178: the filter/order hash segments are the first 8 SHA-256 bytes = exactly 16 hex chars
    // (the comment previously mis-stated "12 bytes"). Lock the segment width.
    [Fact]
    public void BuildKey_HashSegments_Are16HexChars()
    {
        var key = SqlCacheKeyBuilder.BuildKey(Scope, "Users", "x => x.Age > 5", "Name:asc", null, null);

        // sql:<scopeHash>:Users:<filterHash>:<orderHash>:_:_   (SH-H005 inserted the scope segment,
        // which shifts every index below by one — the widths themselves are unchanged.)
        var parts = key.Split(':');
        parts[1].Should().HaveLength(16).And.MatchRegex("^[0-9a-f]{16}$", "the scope segment is hashed too");
        parts[2].Should().Be("Users", "the table stays readable, so a key can be recognised by eye");
        parts[3].Should().HaveLength(16).And.MatchRegex("^[0-9a-f]{16}$");
        parts[4].Should().HaveLength(16).And.MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public void BuildKey_IsDeterministic_AndFilterSensitive()
    {
        var a = SqlCacheKeyBuilder.BuildKey(Scope, "Users", "x => x.Age > 5", null, null, null);
        var b = SqlCacheKeyBuilder.BuildKey(Scope, "Users", "x => x.Age > 5", null, null, null);
        var c = SqlCacheKeyBuilder.BuildKey(Scope, "Users", "x => x.Age > 6", null, null, null);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void GetTablePrefix_IsTableScoped()
    {
        SqlCacheKeyBuilder.GetTablePrefix(Scope, "Users")
            .Should().NotBe(SqlCacheKeyBuilder.GetTablePrefix(Scope, "Orders"));
    }

    // Verifies the invalidation MECHANISM the cached store invokes: removing by a table's prefix drops
    // exactly that table's cached queries and leaves other tables intact.
    [Fact]
    public async Task RemoveByPrefix_InvalidatesOnlyTheTargetTablesQueries()
    {
        using var cache = new MemoryCache();
        var usersKey = SqlCacheKeyBuilder.BuildKey(Scope, "Users", "x => x.Age > 5", null, null, null);
        var usersKey2 = SqlCacheKeyBuilder.BuildKey(Scope, "Users", null, null, 10, 0);
        var ordersKey = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", null, null, null, null);

        var opts = CacheEntryOptions.Absolute(TimeSpan.FromMinutes(5));
        await cache.SetAsync(usersKey, "u1", opts);
        await cache.SetAsync(usersKey2, "u2", opts);
        await cache.SetAsync(ordersKey, "o1", opts);

        await cache.RemoveByPrefixAsync(SqlCacheKeyBuilder.GetTablePrefix(Scope, "Users"));

        (await cache.GetAsync<string>(usersKey)).HasValue.Should().BeFalse();
        (await cache.GetAsync<string>(usersKey2)).HasValue.Should().BeFalse();
        (await cache.GetAsync<string>(ordersKey)).HasValue.Should().BeTrue();
    }
}
