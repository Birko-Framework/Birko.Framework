using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-661, live Cosmos DB: <see cref="AsyncCosmosDBStore{T}.UpdateIfAsync"/> is a compare-and-set. It applies an update
/// only where the condition holds on the stored item, and a lost race (412 on the ETag) is read again, so concurrent
/// increments all land. Gated on <c>BIRKO_COSMOS_CONNECTION</c> (+ <c>BIRKO_COSMOS_CONNECTION_MODE=Gateway</c>).
/// </summary>
public class Task661UpdateIfLiveTests(ITestOutputHelper output)
{
    public class Counter : AbstractModel
    {
        public string? State { get; set; }
        public int Hits { get; set; }
    }

    private AsyncCosmosDBStore<Counter>? Open()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            const string message = "SKIPPED: no live Cosmos. Set BIRKO_COSMOS_CONNECTION; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
            output.WriteLine(message);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException(message);
            return null;
        }
        var settings = new Settings();
        if (string.Equals(Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION_MODE"), "Gateway", StringComparison.OrdinalIgnoreCase))
            settings.ConnectionMode = ConnectionMode.Gateway;
        return new AsyncCosmosDBStore<Counter>(conn, "birko_task661_" + Guid.NewGuid().ToString("N"), null, settings);
    }

    private static async Task DropAsync(AsyncCosmosDBStore<Counter> store)
    {
        try { if (store.Container != null) await store.Container.Database.DeleteAsync(); } catch { }
    }

    [Fact]
    public async Task The_update_applies_only_where_the_condition_holds_on_the_stored_item()
    {
        if (Open() is not { } store) return;
        try
        {
            var id = await store.CreateAsync(new Counter { State = "open" });

            (await store.UpdateIfAsync(id, c => c.State == "closed", c => c.Hits = 99)).Should().BeNull();
            (await store.ReadAsync(id))!.Hits.Should().Be(0, "a condition that does not hold writes nothing");

            var written = await store.UpdateIfAsync(id, c => c.State == "open", c => { c.State = "closed"; c.Hits = 1; });
            written!.State.Should().Be("closed");
            (await store.ReadAsync(id))!.Hits.Should().Be(1);

            (await store.UpdateIfAsync(Guid.NewGuid(), _ => true, c => c.Hits = 5)).Should().BeNull("a missing item is not created");
        }
        finally { await DropAsync(store); }
    }

    [Fact]
    public async Task Concurrent_conditional_increments_all_land()
    {
        if (Open() is not { } store) return;
        try
        {
            var id = await store.CreateAsync(new Counter { State = "open" });
            const int writers = 8;
            await Task.WhenAll(Enumerable.Range(0, writers).Select(_ => Task.Run(() => store.UpdateIfAsync(id, _ => true, c => c.Hits++))));

            (await store.ReadAsync(id))!.Hits.Should().Be(writers, "a lost race is read again, never written over");
        }
        finally { await DropAsync(store); }
    }

    [Fact]
    public async Task It_refuses_to_run_inside_a_transactional_batch()
    {
        if (Open() is not { } store) return;
        try
        {
            var id = await store.CreateAsync(new Counter { State = "open" });
            store.SetTransactionContext(store.Container!.CreateTransactionalBatch(new PartitionKey(id.ToString())));
            var act = () => store.UpdateIfAsync(id, _ => true, c => c.Hits = 1);
            await act.Should().ThrowAsync<InvalidOperationException>();
            store.SetTransactionContext(null);
        }
        finally { await DropAsync(store); }
    }
}
