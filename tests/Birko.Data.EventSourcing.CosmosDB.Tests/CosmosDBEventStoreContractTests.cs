using Birko.Data.CosmosDB.Stores;
using Birko.Data.EventSourcing.CosmosDB;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.EventSourcing;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.EventSourcing.CosmosDB.Tests;

/// <summary>
/// TASK-995 — the shared event store contract on <see cref="CosmosDBEventStore"/>, live, obtained as an application does:
/// <c>factory.GetEventStore(o => o.AddCosmosDB())</c>. A new factory per writer, as a second process has.
/// </summary>
/// <remarks>
/// Gated on <c>BIRKO_COSMOS_CONNECTION</c> (and <c>BIRKO_COSMOS_CONNECTION_MODE</c>, <c>Gateway</c> for the emulator); set
/// <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure. Each test class instance uses a database of its own.
/// </remarks>
public sealed class CosmosDBEventStoreContractTests : EventStoreContract, IDisposable
{
    private static string? Connection => Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
    private readonly ITestOutputHelper _output;
    private readonly string _database = "t995" + Guid.NewGuid().ToString("N")[..16];
    private readonly List<IStoreFactory> _factories = new();

    public CosmosDBEventStoreContractTests(ITestOutputHelper output) => _output = output;

    protected override bool Ready()
    {
        if (!string.IsNullOrWhiteSpace(Connection)) return true;
        const string message = "SKIPPED: no live Cosmos DB. Set BIRKO_COSMOS_CONNECTION (and BIRKO_COSMOS_CONNECTION_MODE=Gateway "
                             + "for the emulator); set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private IStoreFactory NewFactory()
    {
        var mode = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION_MODE") is { Length: > 0 } m ? $";ConnectionMode={m}" : "";
        var factory = Birko.Data.Stores.StoreFactory.Create("CosmosDB", $"{Connection!.TrimEnd(';')};Database={_database}{mode}", p => p.AddCosmosDB());
        _factories.Add(factory);
        return factory;
    }

    protected override Task<IAsyncEventStore> NewStoreAsync() => Task.FromResult(NewFactory().GetEventStore(o => o.AddCosmosDB()));

    private static DomainEvent Event(Guid aggregate, long version) => new(aggregate, version, "Changed", "{}");

    [Fact]
    public void A_CosmosDB_factory_answers_the_CosmosDB_event_store()
    {
        if (!Ready()) return;
        NewFactory().GetEventStore(o => o.AddCosmosDB()).Should().BeOfType<CosmosDBEventStore>();
        NewFactory().GetEventStore().Should().BeOfType<AsyncStoreEventStore>("without the override the default is answered");
    }

    [Fact]
    public async Task A_batch_mixing_aggregates_is_refused_and_keeps_nothing()
    {
        if (!Ready()) return;
        var store = await NewStoreAsync();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var act = () => store.AppendRangeAsync(new IEvent[] { Event(a, 1), Event(b, 1) });
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*one aggregate's events per batch*");
        (await store.ReadAsync(a)).Should().BeEmpty();
        (await store.ReadAsync(b)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_batch_over_the_Cosmos_limit_is_refused()
    {
        if (!Ready()) return;
        var store = await NewStoreAsync();
        var a = Guid.NewGuid();
        var act = () => store.AppendRangeAsync(Enumerable.Range(1, CosmosDBEventStore.MaxBatchSize + 1).Select(v => (IEvent)Event(a, v)));
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*at most 100*");
        (await store.ReadAsync(a)).Should().BeEmpty();
    }

    [Fact]
    public async Task Events_live_in_a_container_partitioned_by_aggregate()
    {
        if (!Ready()) return;
        var factory = (CosmosDBStoreFactory)NewFactory();
        var a = Guid.NewGuid();
        await factory.GetEventStore(o => o.AddCosmosDB()).AppendAsync(Event(a, 1));

        var client = ((AsyncCosmosDBStore<Birko.Data.EventSourcing.Models.EventRecord>)factory.GetAsyncStore<Birko.Data.EventSourcing.Models.EventRecord>()).Client!;
        var container = client.GetContainer(_database, CosmosDBEventStore.ContainerName);
        (await container.ReadContainerAsync()).Resource.PartitionKeyPath.Should().Be("/aggregateGuid");
        var doc = await container.ReadItemAsync<CosmosDBEventStore.EventDocument>("1", new PartitionKey(a.ToString("D")));
        doc.Resource.Version.Should().Be(1, "a document is addressed by its version within the aggregate's partition");
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Connection) || _factories.Count == 0) return;
        try
        {
            var client = ((AsyncCosmosDBStore<Birko.Data.EventSourcing.Models.EventRecord>)_factories[0]
                .GetAsyncStore<Birko.Data.EventSourcing.Models.EventRecord>()).Client!;
            client.GetDatabase(_database).DeleteAsync().GetAwaiter().GetResult();
        }
        catch { }
    }
}
