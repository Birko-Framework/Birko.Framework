using System;
using System.Linq;
using System.Linq.Expressions;
using System.IO;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Memory;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Caching.Tests;

/// <summary>
/// CR-H085: the SQL.Caching store surface was untested. These SQLite-backed tests cover the
/// caching behavior of CachedAsyncDataBaseBulkStore itself: cache hit (a stale result is served
/// after an out-of-band delete), write-through invalidation, and Enabled=false passthrough.
/// </summary>
public class CachedStoreBehaviorTests : IDisposable
{
    private readonly string _root;
    private readonly SqLiteSettings _settings;

    public CachedStoreBehaviorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqlcache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _settings = new SqLiteSettings(_root, "cache.db");

        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMapping());
        registry.ApplyToDatabase();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Widget : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class WidgetMapping : IModelMapping<Widget>
    {
        public void Configure(ModelMap<Widget> map)
        {
            map.ToTable("Widgets").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget> NewStore(SqlCacheOptions options)
    {
        var store = new CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget>(new MemoryCache(), options);
        store.SetSettings(_settings);
        return store;
    }

    private void DeleteAllRowsOutOfBand()
    {
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        using var conn = connector.CreateConnection(connector.Settings);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Widgets";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task Read_IsServedFromCache_ThenInvalidatedByWrite()
    {
        var store = NewStore(new SqlCacheOptions { Enabled = true });
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";

        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a" } });

        (await store.ReadAsync(filter)).Should().HaveCount(1);   // miss -> DB -> cached

        DeleteAllRowsOutOfBand();                                // bypass the cache

        (await store.ReadAsync(filter)).Should().HaveCount(1, "the result is served from cache");

        // A write through the store invalidates the table's cache entries.
        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "b" } });

        (await store.ReadAsync(filter)).Should().BeEmpty("the cache was invalidated, so the DB is re-read");
    }

    [Fact]
    public async Task Disabled_Cache_AlwaysHitsTheDatabase()
    {
        var store = NewStore(new SqlCacheOptions { Enabled = false });
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";

        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a" } });
        (await store.ReadAsync(filter)).Should().HaveCount(1);

        DeleteAllRowsOutOfBand();

        (await store.ReadAsync(filter)).Should().BeEmpty("with caching disabled every read hits the DB");
    }
}
