using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Caching.Memory;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Caching.Tests;

/// <summary>
/// TASK-184 on the SQL cached store: SH-L053 (keys did not name the entity type, so two types over one table shared
/// entries) and SH-L054 (a store with caching disabled did not invalidate, leaving a cache shared with an enabled store
/// stale). Part of <see cref="CachedStoreBehaviorTests"/> so the global table registration is applied by one fixture.
/// </summary>
public partial class CachedStoreBehaviorTests
{
    [Fact]
    public void SH_L053_the_entity_type_is_part_of_the_key_but_not_of_the_invalidation_prefix()
    {
        var a = SqlCacheKeyBuilder.BuildKey("s", "Orders", "f", null, null, null, typeof(Widget));
        var b = SqlCacheKeyBuilder.BuildKey("s", "Orders", "f", null, null, null, typeof(CachedStoreBehaviorTests));
        a.Should().NotBe(b);
        a.Should().StartWith(SqlCacheKeyBuilder.GetTablePrefix("s", "Orders"));
        b.Should().StartWith(SqlCacheKeyBuilder.GetTablePrefix("s", "Orders"));

        SqlCacheKeyBuilder.BuildSingleKey("s", "Orders", "f", typeof(Widget))
            .Should().NotBe(SqlCacheKeyBuilder.BuildSingleKey("s", "Orders", "f", typeof(CachedStoreBehaviorTests)));
    }

    [Fact]
    public async Task SH_L054_a_store_with_caching_disabled_still_invalidates_a_shared_cache()
    {
        var cache = new MemoryCache();
        var cached = new CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget>(cache, new SqlCacheOptions { Enabled = true });
        cached.SetSettings(_settings);
        var uncached = new CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget>(cache, new SqlCacheOptions { Enabled = false });
        uncached.SetSettings(_settings);
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";

        await cached.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a" } });
        (await cached.ReadAsync(filter)).Should().HaveCount(1); // cached

        await uncached.DeleteAsync(filter);

        (await cached.ReadAsync(filter)).Should().BeEmpty("the other store deleted the row through the same cache");
    }
}
