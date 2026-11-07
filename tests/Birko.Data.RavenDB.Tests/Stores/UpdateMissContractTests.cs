using System;
using System.Collections.Generic;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Raven.Client.Documents;
using Raven.Client.ServerWide.Operations;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

public class UpdateMissDoc : AbstractModel
{
    public string? Name { get; set; }
}

/// <summary>Live-server plumbing shared by the sync and async RavenDB contract classes.</summary>
internal sealed class RavenLive : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _databases = new();
    private readonly List<IDisposable> _stores = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");

    public RavenLive(ITestOutputHelper output) => _output = output;

    public bool Ready()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL (e.g. http://localhost:8080); "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public TStore Track<TStore>(Func<string, string, TStore> make) where TStore : IDisposable
    {
        var db = "birko-task555-" + Guid.NewGuid().ToString("N");
        _databases.Add(db);
        var store = make(Url!, db);
        _stores.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (var s in _stores) { try { s.Dispose(); } catch { } }
        if (string.IsNullOrWhiteSpace(Url) || _databases.Count == 0) return;
        try
        {
            using var admin = new DocumentStore { Urls = new[] { Url! } }.Initialize();
            foreach (var db in _databases)
                admin.Maintenance.Server.Send(new DeleteDatabasesOperation(db, hardDelete: true));
        }
        catch { /* cleanup is best-effort */ }
    }
}

/// <summary>TASK-555's cross-provider update-miss contract on <see cref="RavenDBStore{T}"/>, live.</summary>
public sealed class RavenUpdateMissContractTests : StoreContract<UpdateMissDoc>, IDisposable
{
    private readonly RavenLive _live;
    public RavenUpdateMissContractTests(ITestOutputHelper output) => _live = new RavenLive(output);
    protected override bool Ready() => _live.Ready();
    protected override IBulkStore<UpdateMissDoc> NewStore() => _live.Track((url, db) => new RavenDBStore<UpdateMissDoc>(url, db));
    protected override UpdateMissDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissDoc entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissDoc entity, string name) => entity.Name = name;
    public void Dispose() => _live.Dispose();
}

/// <summary>The async twin, on <see cref="AsyncRavenDBStore{T}"/>.</summary>
public sealed class AsyncRavenUpdateMissContractTests : AsyncStoreContract<UpdateMissDoc>, IDisposable
{
    private readonly RavenLive _live;
    public AsyncRavenUpdateMissContractTests(ITestOutputHelper output) => _live = new RavenLive(output);
    protected override bool Ready() => _live.Ready();
    protected override IAsyncBulkStore<UpdateMissDoc> NewStore() => _live.Track((url, db) => new AsyncRavenDBStore<UpdateMissDoc>(url, db));
    protected override UpdateMissDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissDoc entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissDoc entity, string name) => entity.Name = name;
    public void Dispose() => _live.Dispose();
}
