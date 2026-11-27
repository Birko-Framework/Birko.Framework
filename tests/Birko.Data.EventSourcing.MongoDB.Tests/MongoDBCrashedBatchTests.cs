using System.Linq.Expressions;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.MongoDB;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.EventSourcing;
using MongoDB.Driver;
using Xunit.Abstractions;

namespace Birko.Data.EventSourcing.MongoDB.Tests;

/// <summary>A MongoDB store that dies after a number of creates: that create and every later write throw.</summary>
internal sealed class CrashingMongoDBStore : AsyncMongoDBStore<EventRecord>
{
    private int _createsLeft;
    private bool _dead;

    public CrashingMongoDBStore(int createsBeforeCrash) => _createsLeft = createsBeforeCrash;

    private void Gate(bool create)
    {
        if (_dead) throw new IOException("the process is dead");
        if (!create || _createsLeft-- > 0) return;
        _dead = true;
        throw new IOException("the process died");
    }

    protected override Task<Guid> CreateCoreAsync(EventRecord data, StoreDataDelegate<EventRecord>? processDelegate = null, CancellationToken ct = default)
    {
        Gate(true);
        return base.CreateCoreAsync(data, processDelegate, ct);
    }

    protected override Task DeleteCoreAsync(EventRecord data, CancellationToken ct = default)
    {
        Gate(false);
        return base.DeleteCoreAsync(data, ct);
    }

    protected override Task DeleteCoreAsync(IEnumerable<EventRecord> data, CancellationToken ct = default)
    {
        Gate(false);
        return base.DeleteCoreAsync(data, ct);
    }

    public override Task DeleteAsync(Expression<Func<EventRecord, bool>> filter, CancellationToken ct = default)
    {
        Gate(false);
        return base.DeleteAsync(filter, ct);
    }
}

/// <summary>
/// TASK-1000 — a batch cut short by a crash, live on a standalone <c>mongod</c>, where <see cref="MongoDBEventStore"/> has
/// no transactions and appends one event at a time. Gated on <c>BIRKO_MONGO_STANDALONE_URL</c> (locally
/// <c>mongodb://localhost:27018/</c>); CI's live-tests MongoDB job starts one and sets it.
/// </summary>
public sealed class MongoDBStandaloneCrashedBatchTests : CrashedBatchContract, IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _database = "birko_task1000_" + Guid.NewGuid().ToString("N");

    public MongoDBStandaloneCrashedBatchTests(ITestOutputHelper output) => _output = output;

    private static string? ServerUrl
        => Environment.GetEnvironmentVariable("BIRKO_MONGO_STANDALONE_URL") is { Length: > 0 } url ? url : null;

    private string ConnectionString => new MongoUrlBuilder(ServerUrl!) { DatabaseName = _database }.ToString();

    protected override bool Ready()
    {
        if (ServerUrl != null) return true;
        const string message = "SKIPPED: BIRKO_MONGO_STANDALONE_URL is not set; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    protected override Task<IAsyncEventStore> NewStoreAsync()
        => Task.FromResult(Birko.Data.Stores.StoreFactory.Create("MongoDB", ConnectionString, p => p.AddMongoDB())
            .GetEventStore(o => o.AddMongoDB()));

    protected override Task<IAsyncEventStore> NewCrashingWriterAsync(int createsBeforeCrash)
    {
        var store = new CrashingMongoDBStore(createsBeforeCrash);
        store.SetSettings(MongoDBStoreFactory.ParseConnectionString(ConnectionString));
        return Task.FromResult<IAsyncEventStore>(new MongoDBEventStore(store));
    }

    protected override void SetLease(IAsyncEventStore store, TimeSpan lease) => ((AsyncStoreEventStore)store).AbandonedBatchLease = lease;

    public void Dispose()
    {
        if (ServerUrl == null) return;
        try { new MongoClient(ServerUrl).DropDatabase(_database); } catch { }
    }
}
