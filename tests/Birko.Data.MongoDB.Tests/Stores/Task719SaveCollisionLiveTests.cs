using System;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.MongoDB.Stores;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-719, live. Measured before the fix: <c>AsyncMongoDBStore.SaveAsync</c> of an entity carrying a Guid writes with a
/// native upsert, outside the create path that names a duplicate key, so a unique-index collision reached the caller as
/// the driver's <c>MongoWriteException</c> while <c>CreateAsync</c> of the same entity answered
/// <c>StoreUniqueViolationException</c>.
/// </summary>
public sealed class Task719SaveCollisionLiveTests : IDisposable
{
    private readonly MongoLive _live;
    public Task719SaveCollisionLiveTests(ITestOutputHelper output) => _live = new MongoLive(output);

    [Fact]
    public async Task A_save_that_collides_with_a_unique_index_is_a_unique_violation()
    {
        if (!_live.Ready()) return;
        var settings = _live.NewSettings();
        var store = new AsyncMongoDBStore<UpdateMissDoc>();
        store.SetSettings(settings);
        await store.CreateAsync(new UpdateMissDoc { Name = "taken" });
        var collection = new MongoClient($"mongodb://{Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST")}:27017")
            .GetDatabase(settings.Name).GetCollection<UpdateMissDoc>(nameof(UpdateMissDoc));
        await collection.Indexes.CreateOneAsync(new CreateIndexModel<UpdateMissDoc>(
            Builders<UpdateMissDoc>.IndexKeys.Ascending(x => x.Name), new CreateIndexOptions { Unique = true }));

        // The create path names it (the index is in force) ...
        await FluentActions.Awaiting(() => store.CreateAsync(new UpdateMissDoc { Name = "taken" }))
            .Should().ThrowAsync<StoreUniqueViolationException>();
        // ... and so does the native upsert.
        await FluentActions.Awaiting(() => store.SaveAsync(new UpdateMissDoc { Guid = Guid.NewGuid(), Name = "taken" }))
            .Should().ThrowAsync<StoreUniqueViolationException>();
    }

    public void Dispose() => _live.Dispose();
}
