using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-656 (FIELD-047), live: a <see cref="DateTime"/> round-trips to the microsecond on MySQL. The column was
/// <c>DATETIME</c>, which has no fractional digits, so <c>…56.1234560</c> read back <c>…56.0000000</c> (measured on
/// TASK-647), two writes within a second compared equal, and a filter on the exact value matched nothing.
/// Gated on <c>BIRKO_MYSQL_HOST</c>.
/// </summary>
public class Task656DateTimePrecisionLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";

    private readonly ITestOutputHelper _output;
    public Task656DateTimePrecisionLiveTests(ITestOutputHelper output) => _output = output;

    private bool Live()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private static MySqlSettings Settings() => new(Host!, Database, User, Password, Port);

    private static void Exec(string sql)
    {
        using var connection = new MySqlConnection(Settings().GetConnectionString());
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

    // The 7th digit is 0: MySQL keeps microseconds, so a 100 ns tick is beyond any MySQL type.
    private static readonly DateTime Precise = new DateTime(2026, 10, 17, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234560);

    private AsyncDataBaseBulkStore<MySQLConnector, T656Row> Store()
    {
        Exec("DROP TABLE IF EXISTS `T656Times`");
        var store = new AsyncDataBaseBulkStore<MySQLConnector, T656Row>();
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

        read!.Stamp.Ticks.Should().Be(Precise.Ticks, "the sub-second digits survive");
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

    [Fact]
    public void A_column_created_before_the_change_is_reported_as_drift_and_left_to_the_operator()
    {
        if (!Live()) return;
        Exec("DROP TABLE IF EXISTS `T656Times`");
        Exec("CREATE TABLE `T656Times` (Stamp DATETIME NOT NULL, Guid CHAR(36) PRIMARY KEY)");
        var connector = new MySQLConnector(Settings());

        var report = connector.DetectDrift(typeof(T656Row));
        var plan = connector.PlanRepair(typeof(T656Row));

        // Decided on TASK-656: an existing DATETIME column is reported, and a type change stays the operator's, as every
        // type change does since TASK-633. The widening is lossless: ALTER TABLE ... MODIFY Stamp DATETIME(6) NOT NULL.
        report.Drifts.Should().ContainSingle(d => d.Kind == Birko.Data.SQL.SchemaDrift.ColumnDriftKind.TypeMismatch)
            .Which.ToString().Should().Contain("declared DATETIME(6), stored datetime");
        plan.Statements.Should().BeEmpty();
        plan.NotActioned.Should().ContainSingle().Which.Drift.Should().Contain("Stamp");
    }
}
