using System;
using System.Collections.Generic;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using MongoDB.Driver;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

public class UpdateMissDoc : Birko.Data.MongoDB.Models.MongoDBModel
{
    public string? Name { get; set; }
}

/// <summary>Live-server plumbing shared by the sync and async MongoDB contract classes.</summary>
internal sealed class MongoLive : IDisposable
{
    private const string HostEnv = "BIRKO_MONGO_HOST";
    private readonly ITestOutputHelper _output;
    private readonly List<string> _databases = new();
    private string? _host;

    public MongoLive(ITestOutputHelper output) => _output = output;

    public bool Ready()
    {
        _host = Environment.GetEnvironmentVariable(HostEnv);
        if (!string.IsNullOrWhiteSpace(_host)) return true;
        const string message = "SKIPPED: no live MongoDB. Set " + HostEnv + " to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public Settings NewSettings()
    {
        var name = "birko_task555_" + Guid.NewGuid().ToString("N");
        _databases.Add(name);
        return new Settings(_host!, name);
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(_host) || _databases.Count == 0) return;
        try
        {
            var client = new MongoClient($"mongodb://{_host}:27017");
            foreach (var db in _databases) client.DropDatabase(db);
        }
        catch { /* cleanup is best-effort */ }
    }
}

/// <summary>TASK-555's cross-provider update-miss contract on <see cref="MongoDBStore{T}"/>, live.</summary>
public sealed class MongoUpdateMissContractTests : StoreContract<UpdateMissDoc>, IDisposable
{
    private readonly MongoLive _live;
    public MongoUpdateMissContractTests(ITestOutputHelper output) => _live = new MongoLive(output);

    protected override bool Ready() => _live.Ready();

    protected override IBulkStore<UpdateMissDoc> NewStore()
    {
        var store = new MongoDBStore<UpdateMissDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override UpdateMissDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissDoc entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissDoc entity, string name) => entity.Name = name;
    public void Dispose() => _live.Dispose();
}

/// <summary>The async twin, on <see cref="AsyncMongoDBStore{T}"/>.</summary>
public sealed class AsyncMongoUpdateMissContractTests : AsyncStoreContract<UpdateMissDoc>, IDisposable
{
    private readonly MongoLive _live;
    public AsyncMongoUpdateMissContractTests(ITestOutputHelper output) => _live = new MongoLive(output);

    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<UpdateMissDoc> NewStore()
    {
        var store = new AsyncMongoDBStore<UpdateMissDoc>();
        store.SetSettings(_live.NewSettings());
        return store;
    }

    protected override UpdateMissDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissDoc entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissDoc entity, string name) => entity.Name = name;
    public void Dispose() => _live.Dispose();
}
