using System;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Birko.Data.SQL.TimescaleDB.Stores;
using Xunit.Abstractions;

namespace Birko.Data.TimescaleDB.Tests;

/// <summary>Sync and async contract classes run in parallel, so each has its own table.</summary>
[Table("UpdateMissSyncRows")]
public class UpdateMissSyncRow : AbstractModel
{
    public string? Name { get; set; }
    public DateTime Ts { get; set; } = DateTime.UtcNow;
}

[Table("UpdateMissAsyncRows")]
public class UpdateMissAsyncRow : AbstractModel
{
    public string? Name { get; set; }
    public DateTime Ts { get; set; } = DateTime.UtcNow;
}

/// <summary>Live gate for the TimescaleDB contract classes, matching this project's other live tests.</summary>
internal sealed class TimescaleDBLive
{
    private readonly ITestOutputHelper _output;
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_TS_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_TS_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_TS_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_TS_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_TS_DB") ?? "birkoview";

    public TimescaleDBLive(ITestOutputHelper output) => _output = output;

    public bool Ready()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live TimescaleDB. Set BIRKO_TS_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public TimescaleDBSettings Settings() => new(Host!, Database, User, Password, Port, "Ts", "1 day");
}

/// <summary>TASK-555's cross-provider update-miss contract on <see cref="TimescaleDBStore{T}"/>, live. The table is dropped
/// before each test so its counts start from zero.</summary>
public sealed class TimescaleDBUpdateMissContractTests : StoreContract<UpdateMissSyncRow>
{
    protected override bool RefusesDuplicateCreate => false; // TASK-567: a hypertable cannot carry a Guid key (user decision)
    private readonly TimescaleDBLive _live;
    public TimescaleDBUpdateMissContractTests(ITestOutputHelper output) => _live = new TimescaleDBLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IBulkStore<UpdateMissSyncRow> NewStore()
    {
        var store = new TimescaleDBStore<UpdateMissSyncRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }

    protected override UpdateMissSyncRow NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissSyncRow entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissSyncRow entity, string name) => entity.Name = name;
}

/// <summary>The async twin, on <see cref="AsyncTimescaleDBStore{T}"/>.</summary>
public sealed class AsyncTimescaleDBUpdateMissContractTests : AsyncStoreContract<UpdateMissAsyncRow>
{
    protected override bool RefusesDuplicateCreate => false; // TASK-567: a hypertable cannot carry a Guid key (user decision)
    private readonly TimescaleDBLive _live;
    public AsyncTimescaleDBUpdateMissContractTests(ITestOutputHelper output) => _live = new TimescaleDBLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<UpdateMissAsyncRow> NewStore()
    {
        var store = new AsyncTimescaleDBStore<UpdateMissAsyncRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override UpdateMissAsyncRow NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissAsyncRow entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissAsyncRow entity, string name) => entity.Name = name;
}
