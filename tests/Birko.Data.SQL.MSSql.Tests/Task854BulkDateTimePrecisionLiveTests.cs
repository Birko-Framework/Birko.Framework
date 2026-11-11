using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-854: <c>MSSqlConnector.BulkUpdate</c> / <c>BulkDelete</c> created their parameters as
/// <c>new SqlParameter(name, DBNull.Value)</c> and assigned <c>Value</c> per row, bypassing <c>CreateParameter</c>,
/// where SQL Server types a <c>DateTime</c> as <c>DATETIME2</c> (TASK-656). SqlClient inferred <c>datetime</c>, so a
/// written value was rounded to ~3.33 ms and a <c>DateTime</c> key matched no row. Gated on <c>BIRKO_MSSQL_HOST</c>.
/// </summary>
public class Task854BulkDateTimePrecisionLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => LiveDatabase.Name("MSSQL");

    private readonly ITestOutputHelper _output;
    public Task854BulkDateTimePrecisionLiveTests(ITestOutputHelper output) => _output = output;

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

    /// <summary>The stored <c>Stamp</c> of every row, keyed by <c>Seq</c>, read straight from the table.</summary>
    private static Dictionary<int, DateTime?> Stamps(string table)
    {
        var result = new Dictionary<int, DateTime?>();
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Seq, Stamp FROM [" + table + "]";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
        return result;
    }

    [Table("T854Update")]
    public class T854Row : AbstractModel
    {
        public int Seq { get; set; }
        public DateTime? Stamp { get; set; }
    }

    /// <summary>A composite key (inherited <c>Guid</c> plus <c>Stamp</c>), so the delete matches on a DateTime.</summary>
    [Table("T854Delete")]
    public class T854Keyed : AbstractModel
    {
        public int Seq { get; set; }

        [PrimaryField]
        public DateTime Stamp { get; set; }
    }

    private static readonly DateTime Precise = new DateTime(2026, 10, 17, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);

    private static MSSqlConnector Connector<T>(string table)
    {
        Exec("DROP TABLE IF EXISTS [" + table + "]");
        var connector = new MSSqlConnector(Settings());
        connector.CreateTable(new[] { typeof(T) });
        return connector;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkUpdate_stores_every_tick_including_after_a_null_row(bool async)
    {
        if (!Live()) return;
        var connector = Connector<T854Row>("T854Update");
        var rows = new[]
        {
            new T854Row { Guid = Guid.NewGuid(), Seq = 1 },
            new T854Row { Guid = Guid.NewGuid(), Seq = 2 },
            new T854Row { Guid = Guid.NewGuid(), Seq = 3 },
        };
        connector.Insert(DataBase.LoadTable(typeof(T854Row)), rows);

        // A value first, then null, then a value: the row after the null is the re-bind TASK-852 fixed for inserts.
        rows[0].Stamp = Precise;
        rows[1].Stamp = null;
        rows[2].Stamp = Precise.AddTicks(1);
        var unmatched = async
            ? await connector.BulkUpdateAsync(typeof(T854Row), rows)
            : connector.BulkUpdate(typeof(T854Row), rows);

        unmatched.Should().BeEmpty();
        var stored = Stamps("T854Update");
        _output.WriteLine($"sent {Precise:O}, stored {stored[1]:O}; sent {Precise.AddTicks(1):O}, stored {stored[3]:O}");
        stored[1].Should().Be(Precise);
        stored[2].Should().BeNull();
        stored[3].Should().Be(Precise.AddTicks(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkUpdate_after_a_null_first_row_stores_every_tick(bool async)
    {
        if (!Live()) return;
        var connector = Connector<T854Row>("T854Update");
        var rows = new[]
        {
            new T854Row { Guid = Guid.NewGuid(), Seq = 1, Stamp = Precise },
            new T854Row { Guid = Guid.NewGuid(), Seq = 2 },
        };
        connector.Insert(DataBase.LoadTable(typeof(T854Row)), rows);

        rows[0].Stamp = null;
        rows[1].Stamp = Precise;
        if (async) await connector.BulkUpdateAsync(typeof(T854Row), rows);
        else connector.BulkUpdate(typeof(T854Row), rows);

        var stored = Stamps("T854Update");
        _output.WriteLine($"sent {Precise:O}, stored {stored[2]:O}");
        stored[1].Should().BeNull();
        stored[2].Should().Be(Precise);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkDelete_matches_a_DateTime_key_to_the_tick(bool async)
    {
        if (!Live()) return;
        var connector = Connector<T854Keyed>("T854Delete");
        var rows = new[]
        {
            new T854Keyed { Guid = Guid.NewGuid(), Seq = 1, Stamp = Precise },
            new T854Keyed { Guid = Guid.NewGuid(), Seq = 2, Stamp = Precise.AddTicks(1) },
            new T854Keyed { Guid = Guid.NewGuid(), Seq = 3, Stamp = Precise.AddTicks(2) },
        };
        connector.Insert(DataBase.LoadTable(typeof(T854Keyed)), rows);
        Stamps("T854Delete").Should().HaveCount(3, "the insert stores every row");

        var doomed = new[] { rows[0], rows[2] };
        if (async) await connector.BulkDeleteAsync(typeof(T854Keyed), doomed);
        else connector.BulkDelete(typeof(T854Keyed), doomed);

        var remaining = Stamps("T854Delete");
        _output.WriteLine("remaining seqs: " + string.Join(", ", remaining.Keys));
        remaining.Keys.Should().BeEquivalentTo(new[] { 2 });
    }
}
