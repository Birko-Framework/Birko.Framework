using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Caching.Memory;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Caching.Tests;

/// <summary>
/// TASK-498 — <c>CachedAsyncDataBaseBulkStore.UpdateAsync(filter, PropertyUpdate)</c> with an increment, on real
/// SQLite. A cached read is proved live first (an out-of-band write is NOT seen), then the increment goes through the
/// store: the next read must return the stored value plus the delta — neither the stale cached value (no
/// invalidation) nor the bare delta (increment assigned).
/// <para>
/// The model is attribute-mapped on its own table rather than registered through <c>ModelMapRegistry</c>, so this
/// class does not add a second mutator of that process-wide registration beside <c>CachedStoreBehaviorTests</c>.
/// </para>
/// </summary>
public class PropertyUpdateIncrementCachingTests : IDisposable
{
    private readonly string _root;
    private readonly SqLiteSettings _settings;

    public PropertyUpdateIncrementCachingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqlcache-increment-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _settings = new SqLiteSettings(_root, "increment.db");

        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "increment.db" });
        ((SqLiteConnector)factory.GetConnector()).CreateTable(new[] { typeof(CachedCounter) });
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("CachedCounters")]
    public class CachedCounter : AbstractModel
    {
        public string? Name { get; set; }
        public int Hits { get; set; }
    }

    private static readonly Expression<Func<CachedCounter, bool>> Target = x => x.Name == "target";

    private CachedAsyncDataBaseBulkStore<SqLiteConnector, CachedCounter> NewStore()
    {
        var store = new CachedAsyncDataBaseBulkStore<SqLiteConnector, CachedCounter>(new MemoryCache(), new SqlCacheOptions { Enabled = true });
        store.SetSettings(_settings);
        return store;
    }

    private void SetHitsOutOfBand(int hits)
    {
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        using var conn = connector.CreateConnection(connector.Settings);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE CachedCounters SET Hits = {hits}";
        cmd.ExecuteNonQuery();
    }

    private async Task<CachedAsyncDataBaseBulkStore<SqLiteConnector, CachedCounter>> SeedAsync()
    {
        var store = NewStore();
        await store.CreateAsync(new[]
        {
            new CachedCounter { Guid = Guid.NewGuid(), Name = "target", Hits = 10 },
            new CachedCounter { Guid = Guid.NewGuid(), Name = "bystander", Hits = 10 },
        });
        return store;
    }

    [Fact]
    public async Task Increment_Invalidates_A_Cached_Collection_Read()
    {
        var store = await SeedAsync();
        (await store.ReadAsync(Target)).Single().Hits.Should().Be(10);   // miss -> DB -> cached

        SetHitsOutOfBand(20);
        (await store.ReadAsync(Target)).Single().Hits.Should().Be(10, "the cache is live: an out-of-band write is not seen");

        await store.UpdateAsync(Target, new PropertyUpdate<CachedCounter>().Increment(x => x.Hits, 5));

        (await store.ReadAsync(Target)).Single().Hits.Should().Be(25,
            "20 + 5 from the database — 10 would be the stale cache entry, 5 an assigned delta");
        (await store.ReadAsync(x => x.Name == "bystander")).Single().Hits.Should().Be(20, "the filter scoped the increment");
    }

    [Fact]
    public async Task Decrement_Invalidates_A_Cached_Single_Read()
    {
        var store = await SeedAsync();
        (await store.ReadFirstAsync(Target))!.Hits.Should().Be(10);

        SetHitsOutOfBand(20);
        (await store.ReadFirstAsync(Target))!.Hits.Should().Be(10, "the cache is live: an out-of-band write is not seen");

        await store.UpdateAsync(Target, new PropertyUpdate<CachedCounter>().Decrement(x => x.Hits, 3));

        (await store.ReadFirstAsync(Target))!.Hits.Should().Be(17, "20 - 3 from the database, not the cached 10");
    }
}
