using System;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Exceptions;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-719, live. Measured before the fix: a bulk update inside a transactional batch skipped an entity with no Guid
/// and reported nothing, where the same call outside a batch answers <see cref="EntityNotFoundException"/>. The batch is
/// never executed here; the refusal is decided while it is assembled.
/// </summary>
public sealed class Task719BatchUpdateTests : IDisposable
{
    private readonly CosmosLive _live;
    public Task719BatchUpdateTests(ITestOutputHelper output) => _live = new CosmosLive(output);

    [Fact]
    public async Task A_batched_bulk_update_reports_an_entity_with_no_guid()
    {
        if (!_live.Ready()) return;
        var (conn, db, settings) = _live.Next();
        var store = new AsyncCosmosDBStore<UpdateMissDoc>(conn, db, null, settings);
        var stored = new UpdateMissDoc { Name = "stored" };
        await store.CreateAsync(stored);

        using var client = new CosmosClient(conn, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        store.SetTransactionContext(client.GetDatabase(db).GetContainer(nameof(UpdateMissDoc))
            .CreateTransactionalBatch(new PartitionKey(stored.Guid!.Value.ToString())));

        await FluentActions.Awaiting(() => store.UpdateAsync(new[] { stored, new UpdateMissDoc { Name = "unsaved" } }))
            .Should().ThrowAsync<EntityNotFoundException>();
    }

    public void Dispose() => _live.Dispose();
}
