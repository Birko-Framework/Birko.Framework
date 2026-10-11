using System;
using System.Collections.Generic;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Microsoft.Azure.Cosmos;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

public class UpdateMissDoc : AbstractModel
{
    public string? Name { get; set; }
}

/// <summary>
/// Live plumbing for the CosmosDB contract classes, gated like <c>CosmosFilterMatrixLiveTests</c> on
/// <c>BIRKO_COSMOS_CONNECTION</c> (+ <c>BIRKO_COSMOS_CONNECTION_MODE=Gateway</c> for the Docker emulator).
/// Each store gets its own database, deleted afterwards.
/// </summary>
internal sealed class CosmosLive : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _databases = new();
    private static string? Conn => Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
    private static bool Gateway => string.Equals(Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION_MODE"), "Gateway", StringComparison.OrdinalIgnoreCase);

    public CosmosLive(ITestOutputHelper output) => _output = output;

    public bool Ready()
    {
        if (!string.IsNullOrWhiteSpace(Conn)) return true;
        const string message = "SKIPPED: no live CosmosDB. Set BIRKO_COSMOS_CONNECTION (and BIRKO_COSMOS_CONNECTION_MODE=Gateway "
                             + "for the emulator); set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public (string Conn, string Db, Settings Settings) Next()
    {
        var db = "birko_task555_" + Guid.NewGuid().ToString("N");
        _databases.Add(db);
        var settings = new Settings();
        if (Gateway) settings.ConnectionMode = ConnectionMode.Gateway;
        return (Conn!, db, settings);
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Conn) || _databases.Count == 0) return;
        try
        {
            using var client = new CosmosClient(Conn, new CosmosClientOptions { ConnectionMode = Gateway ? ConnectionMode.Gateway : ConnectionMode.Direct });
            foreach (var db in _databases)
            {
                try { client.GetDatabase(db).DeleteAsync().GetAwaiter().GetResult(); } catch { }
            }
        }
        catch { /* cleanup is best-effort */ }
    }
}

/// <summary>TASK-555's cross-provider update-miss contract on <see cref="CosmosDBStore{T}"/>, live.</summary>
public sealed class CosmosUpdateMissContractTests : UpdateMissContract<UpdateMissDoc>, IDisposable
{
    private readonly CosmosLive _live;
    public CosmosUpdateMissContractTests(ITestOutputHelper output) => _live = new CosmosLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IBulkStore<UpdateMissDoc> NewStore()
    {
        var (conn, db, settings) = _live.Next();
        return new CosmosDBStore<UpdateMissDoc>(conn, db, null, settings);
    }

    protected override UpdateMissDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissDoc entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissDoc entity, string name) => entity.Name = name;
    public void Dispose() => _live.Dispose();
}

/// <summary>The async twin, on <see cref="AsyncCosmosDBStore{T}"/>.</summary>
public sealed class AsyncCosmosUpdateMissContractTests : AsyncUpdateMissContract<UpdateMissDoc>, IDisposable
{
    private readonly CosmosLive _live;
    public AsyncCosmosUpdateMissContractTests(ITestOutputHelper output) => _live = new CosmosLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<UpdateMissDoc> NewStore()
    {
        var (conn, db, settings) = _live.Next();
        return new AsyncCosmosDBStore<UpdateMissDoc>(conn, db, null, settings);
    }

    protected override UpdateMissDoc NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissDoc entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissDoc entity, string name) => entity.Name = name;
    public void Dispose() => _live.Dispose();
}
