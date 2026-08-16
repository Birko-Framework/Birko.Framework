using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Models;
using Birko.Data.MongoDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-214 — end-to-end proof that an entity actually reaches the server and comes back. Measured
/// against MongoDB 7, both stores wrote <b>nothing</b>: the sync store threw on the unfreezable
/// <c>MongoDBModel</c> class map, the async store threw on <c>GuidRepresentation.Unspecified</c>, and a
/// read-back returned 0 rows.
///
/// The non-gated half of this coverage lives in
/// <c>Serialization.MongoSerializationTests</c> — class-mapping and BSON round-trip need no server, and
/// keeping them ungated is the point, since this defect survived precisely because the only suite that
/// exercised serialization was gated off. These tests add what genuinely needs a server: that the write
/// is accepted and the document reads back as the same entity.
///
/// Gated on <c>BIRKO_MONGO_HOST</c> (e.g. <c>localhost</c>); no-op pass when absent so CI stays green.
/// </summary>
public class MongoStoreRoundTripLiveTests
{
    private const string HostEnv = "BIRKO_MONGO_HOST";

    public class SyncDoc : MongoDBModel { public string? Name { get; set; } }

    public class AsyncDoc : AbstractModel { public string? Name { get; set; } }

    [Fact]
    public void Sync_store_round_trips_a_MongoDBModel()
    {
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (string.IsNullOrWhiteSpace(host)) return;

        var store = new MongoDBStore<SyncDoc>();
        store.SetSettings(new Settings(host, "birko_task214_sync_" + Guid.NewGuid().ToString("N")));
        try
        {
            var id = store.Create(new SyncDoc { Name = "alpha" });
            id.Should().NotBe(Guid.Empty);

            var read = store.Read().ToList();
            read.Should().ContainSingle();
            read[0].Guid.Should().Be(id);
            read[0].Name.Should().Be("alpha");

            // The canonical id must be usable as a filter, not merely storable.
            store.ReadFirst(x => x.Guid == id)!.Name.Should().Be("alpha");
        }
        finally
        {
            store.Destroy();
        }
    }

    [Fact]
    public async Task Async_store_round_trips_an_AbstractModel()
    {
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (string.IsNullOrWhiteSpace(host)) return;

        var store = new AsyncMongoDBStore<AsyncDoc>();
        store.SetSettings(new Settings(host, "birko_task214_async_" + Guid.NewGuid().ToString("N")));
        try
        {
            var entity = new AsyncDoc { Name = "beta" };
            await store.CreateAsync(entity);
            entity.Guid.Should().NotBeNull();

            var read = (await store.ReadAsync(CancellationToken.None)).ToList();
            read.Should().ContainSingle();
            read[0].Guid.Should().Be(entity.Guid);
            read[0].Name.Should().Be("beta");
        }
        finally
        {
            await store.DestroyAsync();
        }
    }
}
