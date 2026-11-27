using System.Linq.Expressions;
using System.Net.Http;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.EventSourcing.ElasticSearch;
using Birko.Data.EventSourcing.Events;
using Birko.Data.EventSourcing.Models;
using Birko.Data.EventSourcing.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.EventSourcing;
using Xunit.Abstractions;

namespace Birko.Data.EventSourcing.ElasticSearch.Tests;

/// <summary>An Elasticsearch store that dies after a number of creates: that create and every later write throw.</summary>
internal sealed class CrashingElasticSearchStore : AsyncElasticSearchStore<EventRecord>
{
    private int _createsLeft;
    private bool _dead;

    public CrashingElasticSearchStore(int createsBeforeCrash) => _createsLeft = createsBeforeCrash;

    private void Gate(bool create)
    {
        if (_dead) throw new IOException("the process is dead");
        if (!create || _createsLeft-- > 0) return;
        _dead = true;
        throw new IOException("the process died");
    }

    protected override Task<Guid> CreateCoreAsync(EventRecord data, StoreDataDelegate<EventRecord>? storeDelegate = null, CancellationToken ct = default)
    {
        Gate(true);
        return base.CreateCoreAsync(data, storeDelegate, ct);
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
/// TASK-1000 — a batch cut short by a crash, live on <see cref="ElasticSearchEventStore"/>. Gated on <c>BIRKO_ES_URL</c>;
/// set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.
/// </summary>
public sealed class ElasticSearchCrashedBatchTests : CrashedBatchContract, IDisposable
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");
    private readonly ITestOutputHelper _output;
    private readonly string _prefix = "birko1000" + Guid.NewGuid().ToString("N");

    public ElasticSearchCrashedBatchTests(ITestOutputHelper output) => _output = output;

    private string ConnectionString => $"Url={Url};IndexPrefix={_prefix}";

    protected override bool Ready()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live Elasticsearch. Set BIRKO_ES_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    protected override Task<IAsyncEventStore> NewStoreAsync()
        => Task.FromResult(Birko.Data.Stores.StoreFactory.Create("ElasticSearch", ConnectionString, p => p.AddElasticSearch())
            .GetEventStore(o => o.AddElasticSearch()));

    protected override Task<IAsyncEventStore> NewCrashingWriterAsync(int createsBeforeCrash)
    {
        var store = new CrashingElasticSearchStore(createsBeforeCrash);
        store.SetSettings(ElasticSearchStoreFactory.ParseConnectionString(ConnectionString));
        return Task.FromResult<IAsyncEventStore>(new ElasticSearchEventStore(store));
    }

    protected override void SetLease(IAsyncEventStore store, TimeSpan lease) => ((AsyncStoreEventStore)store).AbandonedBatchLease = lease;

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try { Http.DeleteAsync(Url!.TrimEnd('/') + "/" + _prefix + "*").GetAwaiter().GetResult(); } catch { }
    }
}
