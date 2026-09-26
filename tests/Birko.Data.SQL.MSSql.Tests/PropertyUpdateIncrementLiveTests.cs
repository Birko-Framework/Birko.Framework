using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-498 — <c>PropertyUpdate.Increment</c> / <c>Decrement</c> against a live SQL Server; the live twin of
/// <c>Birko.Data.SQL.SqLite.Tests.PropertyUpdateIncrementEndToEndTests</c>.
/// </summary>
/// <remarks>
/// <para>
/// The decimal column declares its precision because a bare <c>DECIMAL</c> is <c>DECIMAL(18,0)</c> here and
/// would truncate <c>10.10</c> before any increment ran.
/// </para>
/// <para>Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</para>
/// </remarks>
public class PropertyUpdateIncrementLiveTests : IDisposable
{
    private const string CounterTable = "MsPropertyUpdateIncrement";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public PropertyUpdateIncrementLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port)
    {
        TrustServerCertificate = true
    };

    [Table(CounterTable)]
    public class Counter : AbstractModel
    {
        [MaxLengthField(64)]
        public string Name { get; set; } = null!;
        public int HitCount { get; set; }
        public long Total { get; set; }

        [PrecisionField(18)]
        [ScaleField(2)]
        public decimal Price { get; set; }
    }

    private static void Exec(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS [{CounterTable}]"); } catch { }
    }

    private static AsyncMSSqlStore<Counter> NewStore()
    {
        var store = new AsyncMSSqlStore<Counter>();
        store.SetSettings(Settings());
        return store;
    }

    private static async Task<(AsyncMSSqlStore<Counter> Store, Guid Target, Guid Bystander)> SeedAsync()
    {
        Exec($"DROP TABLE IF EXISTS [{CounterTable}]");
        new MSSqlConnector(Settings()).CreateTable(new[] { typeof(Counter) });

        var store = NewStore();
        var target = new Counter { Guid = Guid.NewGuid(), Name = "target", HitCount = 10, Total = 100, Price = 10.10m };
        var bystander = new Counter { Guid = Guid.NewGuid(), Name = "bystander", HitCount = 10, Total = 100, Price = 10.10m };
        await store.CreateAsync(target);
        await store.CreateAsync(bystander);
        return (store, target.Guid!.Value, bystander.Guid!.Value);
    }

    private static async Task<Counter> ReadAsync(AsyncMSSqlStore<Counter> store, Guid guid)
        => (await store.ReadAsync(x => x.Guid == guid)).Single();

    [Fact]
    public async Task SetAndIncrement_ApplyToTheTargetRow_AndLeaveOthersUntouched()
    {
        if (!RequireServer()) return;
        var (store, target, bystander) = await SeedAsync();

        await store.UpdateAsync(x => x.Guid == target,
            new PropertyUpdate<Counter>().Set(x => x.Name, "renamed").Increment(x => x.HitCount, 1).Increment(x => x.Total, 5L));

        var after = await ReadAsync(store, target);
        after.Name.Should().Be("renamed");
        after.HitCount.Should().Be(11);
        after.Total.Should().Be(105);

        var untouched = await ReadAsync(store, bystander);
        untouched.Name.Should().Be("bystander");
        untouched.HitCount.Should().Be(10);
    }

    [Fact]
    public async Task Decrement_Subtracts()
    {
        if (!RequireServer()) return;
        var (store, target, _) = await SeedAsync();

        await store.UpdateAsync(x => x.Guid == target, new PropertyUpdate<Counter>().Decrement(x => x.HitCount, 3));

        (await ReadAsync(store, target)).HitCount.Should().Be(7);
    }

    [Fact]
    public async Task DecimalIncrement_OnDeclaredPrecision_IsExact()
    {
        if (!RequireServer()) return;
        var (store, target, _) = await SeedAsync();

        await store.UpdateAsync(x => x.Guid == target, new PropertyUpdate<Counter>().Increment(x => x.Price, 0.20m));

        (await ReadAsync(store, target)).Price.Should().Be(10.30m, "DECIMAL(18,2) adds exactly, unlike SQLite");
    }

    [Fact]
    public async Task ParallelIncrements_EachOnItsOwnStore_AreNotLost()
    {
        if (!RequireServer()) return;
        const int callers = 20;
        var (_, target, _) = await SeedAsync();

        await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(() =>
            NewStore().UpdateAsync(x => x.Guid == target, new PropertyUpdate<Counter>().Increment(x => x.HitCount, 1)))));

        (await ReadAsync(NewStore(), target)).HitCount.Should().Be(10 + callers);
    }
}
