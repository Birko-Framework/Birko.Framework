using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.MongoDB;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.UnitOfWork;
using Birko.Data.Stores;
using Birko.Tests.Shared.EventSourcing;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.EventSourcing.MongoDB.Tests;

/// <summary>Builds the MongoDB factories for one test class: a database of its own, dropped on dispose.</summary>
public abstract class MongoEventStoreContractBase : EventStoreContract, IDisposable
{
    private readonly ITestOutputHelper _output;
    protected readonly string Database = "birko_task995_" + Guid.NewGuid().ToString("N");

    protected MongoEventStoreContractBase(ITestOutputHelper output) => _output = output;

    /// <summary>The server's connection string without a database, or null when the variable is not set.</summary>
    protected abstract string? ServerUrl { get; }

    protected abstract string Variable { get; }

    protected override bool Ready()
    {
        if (ServerUrl != null) return true;
        var message = $"SKIPPED: {Variable} is not set; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    // mongodb://host:port/?options -> mongodb://host:port/<database>?options
    protected IStoreFactory NewFactory()
    {
        var url = new MongoUrlBuilder(ServerUrl!) { DatabaseName = Database }.ToString();
        return Birko.Data.Stores.StoreFactory.Create("MongoDB", url, p => p.AddMongoDB());
    }

    protected override Task<IAsyncEventStore> NewStoreAsync() => Task.FromResult(NewFactory().GetEventStore(o => o.AddMongoDB()));

    [Fact]
    public void A_MongoDB_factory_answers_the_MongoDB_event_store()
    {
        if (!Ready()) return;
        NewFactory().GetEventStore(o => o.AddMongoDB()).Should().BeOfType<MongoDBEventStore>();
        NewFactory().GetEventStore().Should().BeOfType<AsyncStoreEventStore>("without the override the default is answered");
    }

    public void Dispose()
    {
        if (ServerUrl == null) return;
        try { new MongoClient(ServerUrl).DropDatabase(Database); } catch { }
    }
}

/// <summary>
/// TASK-995 — the event store contract on a standalone <c>mongod</c>, which has no transactions, so
/// <see cref="MongoDBEventStore"/> falls back to the default one-event-at-a-time append. Gated on
/// <c>BIRKO_MONGO_STANDALONE_URL</c>, a connection string without a database (locally <c>mongodb://localhost:27018/</c>).
/// CI's live-tests MongoDB job starts one beside the replica set and sets it.
/// </summary>
public sealed class MongoDBStandaloneEventStoreContractTests : MongoEventStoreContractBase
{
    public MongoDBStandaloneEventStoreContractTests(ITestOutputHelper output) : base(output) { }

    protected override string Variable => "BIRKO_MONGO_STANDALONE_URL";

    protected override string? ServerUrl
        => Environment.GetEnvironmentVariable("BIRKO_MONGO_STANDALONE_URL") is { Length: > 0 } url ? url : null;
}

/// <summary>
/// TASK-995 — the event store contract on a replica set, where a batch is one transaction. Gated on
/// <c>BIRKO_MONGO_HOST</c> (and <c>BIRKO_MONGO_PORT</c>, default 27017), the replica set every MongoDB suite uses, in CI and
/// locally.
/// </summary>
public sealed class MongoDBReplicaSetEventStoreContractTests : MongoEventStoreContractBase
{
    public MongoDBReplicaSetEventStoreContractTests(ITestOutputHelper output) : base(output) { }

    protected override string Variable => "BIRKO_MONGO_HOST";

    protected override string? ServerUrl
    {
        get
        {
            var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
            if (string.IsNullOrWhiteSpace(host)) return null;
            var port = Environment.GetEnvironmentVariable("BIRKO_MONGO_PORT") is { Length: > 0 } p ? p : "27017";
            return $"mongodb://{host}:{port}/";
        }
    }

    [Fact]
    public async Task An_append_inside_the_callers_transaction_is_undone_with_it()
    {
        if (!Ready()) return;
        var factory = NewFactory();
        var events = factory.GetEventStore(o => o.AddMongoDB());
        var store = (AsyncMongoDBStore<EventRecord>)factory.GetAsyncStore<EventRecord>();
        await store.InitAsync();
        var a = Guid.NewGuid();

        await using (var uow = MongoDbUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await events.AppendAsync(new DomainEvent(a, 1, "Inside", "{}"));
            await uow.RollbackAsync();
        }

        (await (await NewStoreAsync()).ReadAsync(a)).Should().BeEmpty("the append joined the caller's transaction");
    }
}
