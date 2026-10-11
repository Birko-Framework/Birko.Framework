using System;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;
using Birko.Data.SQL.PostgreSQL.Stores;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>Sync and async contract classes run in parallel, so each has its own table.</summary>
[Table("UpdateMissSyncRows")]
public class UpdateMissSyncRow : AbstractModel
{
    public string? Name { get; set; }
}

[Table("UpdateMissAsyncRows")]
public class UpdateMissAsyncRow : AbstractModel
{
    public string? Name { get; set; }
}

/// <summary>Live gate for the PostgreSQL contract classes, matching this project's other live tests.</summary>
internal sealed class PostgreSQLLive
{
    private readonly ITestOutputHelper _output;
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";

    public PostgreSQLLive(ITestOutputHelper output) => _output = output;

    public bool Ready()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };
}

/// <summary>TASK-555's cross-provider update-miss contract on <see cref="PostgreSQLStore{T}"/>, live. The table is dropped
/// before each test so its counts start from zero.</summary>
public sealed class PostgreSQLUpdateMissContractTests : StoreContract<UpdateMissSyncRow>
{
    private readonly PostgreSQLLive _live;
    public PostgreSQLUpdateMissContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IBulkStore<UpdateMissSyncRow> NewStore()
    {
        var store = new PostgreSQLStore<UpdateMissSyncRow>();
        store.SetSettings(_live.Settings());
        store.Destroy();
        return store;
    }

    protected override UpdateMissSyncRow NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissSyncRow entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissSyncRow entity, string name) => entity.Name = name;
}

/// <summary>The async twin, on <see cref="AsyncPostgreSQLStore{T}"/>.</summary>
public sealed class AsyncPostgreSQLUpdateMissContractTests : AsyncStoreContract<UpdateMissAsyncRow>
{
    private readonly PostgreSQLLive _live;
    public AsyncPostgreSQLUpdateMissContractTests(ITestOutputHelper output) => _live = new PostgreSQLLive(output);
    protected override bool Ready() => _live.Ready();

    protected override IAsyncBulkStore<UpdateMissAsyncRow> NewStore()
    {
        var store = new AsyncPostgreSQLStore<UpdateMissAsyncRow>();
        store.SetSettings(_live.Settings());
        store.DestroyAsync().GetAwaiter().GetResult();
        return store;
    }

    protected override UpdateMissAsyncRow NewEntity(string name) => new() { Name = name };
    protected override string NameOf(UpdateMissAsyncRow entity) => entity.Name ?? "";
    protected override void Rename(UpdateMissAsyncRow entity, string name) => entity.Name = name;
}
