using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-656 (FIELD-047), live: a <see cref="DateTime"/> round-trips exactly on SQL Server. The column is
/// <c>DATETIME2</c>, but the value was bound as an inferred <c>datetime</c> parameter, so <c>…56.1234567</c> read back
/// <c>…56.1233333</c> (measured on TASK-647) and a filter on the exact value matched nothing. Gated on
/// <c>BIRKO_MSSQL_HOST</c>.
/// </summary>
public class Task656DateTimePrecisionLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";

    private readonly ITestOutputHelper _output;
    public Task656DateTimePrecisionLiveTests(ITestOutputHelper output) => _output = output;

    private bool Live()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port) { TrustServerCertificate = true };

    private static void Exec(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Table("T656Times")]
    public class T656Row : AbstractModel
    {
        public DateTime Stamp { get; set; }
    }

    // DATETIME2 keeps 100 ns ticks, so every digit survives.
    private static readonly DateTime Precise = new DateTime(2026, 10, 17, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);

    private AsyncDataBaseBulkStore<MSSqlConnector, T656Row> Store()
    {
        Exec("DROP TABLE IF EXISTS [T656Times]");
        var store = new AsyncDataBaseBulkStore<MSSqlConnector, T656Row>();
        store.SetSettings(Settings());
        return store;
    }

    [Fact]
    public async Task A_DateTime_round_trips_to_the_microsecond_and_is_found_by_its_exact_value()
    {
        if (!Live()) return;
        var store = Store();
        var row = new T656Row { Stamp = Precise };
        await store.CreateAsync(row);

        var read = await store.ReadFirstAsync(x => x.Guid == row.Guid);

        read!.Stamp.Ticks.Should().Be(Precise.Ticks, "every tick survives in a DATETIME2 column");
        (await store.ReadAsync(x => x.Stamp == Precise, null, null, null)).Should().ContainSingle("a filter on the exact value matches");
    }

    [Fact]
    public async Task Two_writes_within_a_second_stay_ordered()
    {
        if (!Live()) return;
        var store = Store();
        var first = new T656Row { Stamp = Precise };
        var second = new T656Row { Stamp = Precise.AddTicks(4000) }; // 0.4 ms later
        await store.CreateAsync(new[] { second, first });

        var ordered = (await store.ReadAsync(x => true, OrderBy<T656Row>.By(x => x.Stamp), null, null)).ToList();

        ordered.Select(r => r.Guid).Should().Equal(first.Guid, second.Guid);
        ordered[0].Stamp.Should().BeBefore(ordered[1].Stamp);
    }
}
