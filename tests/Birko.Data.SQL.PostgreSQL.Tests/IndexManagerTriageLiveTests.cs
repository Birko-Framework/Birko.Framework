using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-154 on PostgreSQL: what the index manager reports for direction (SH-M232 / SH-M244) and for a same-named table
/// outside the connection's schema or database (SH-M227).
/// </summary>
/// <remarks>Gated on the provider's host variable; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</remarks>
public class IndexManagerTriageLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public IndexManagerTriageLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    private static void Exec(string sql)
    {
        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Table("PgIdxDir")]
    public class Row : AbstractModel
    {
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
        public int D { get; set; }
        [IndexedField("IX_PgIdxDir_E", 0, true)] public int E { get; set; }
        [IndexedField("IX_PgIdxDir_Mix", 0)] public int F { get; set; }
        [IndexedField("IX_PgIdxDir_Mix", 1, true)] public int G { get; set; }
    }

    private PostgreSQLConnector Created()
    {
        Exec("DROP TABLE IF EXISTS \"PgIdxDir\"");
        var connector = new PostgreSQLConnector(Settings());
        connector.CreateTable(new[] { typeof(Row) });
        return connector;
    }

    [Fact]
    public async Task A_descending_column_is_listed_descending_wherever_it_sits()
    {
        if (!RequireServer()) return;
        var listed = (await new Birko.Data.SQL.PostgreSQL.IndexManagement.PostgreSqlIndexManager(Created()).ListAsync("PgIdxDir")).ToDictionary(i => i.Name);

        listed["IX_PgIdxDir_E"].Fields.Single().IsDescending.Should().BeTrue("E is the table's 6th column and the index's only one");
        listed["IX_PgIdxDir_Mix"].Fields.Select(f => (f.Name.ToUpperInvariant(), f.IsDescending)).Should().Equal(("F", false), ("G", true));
        listed.Keys.Should().NotContain("PRIMARY");
    }

    [Fact]
    public async Task A_same_named_table_elsewhere_does_not_answer_for_this_one()
    {
        if (!RequireServer()) return;
        var manager = new Birko.Data.SQL.PostgreSQL.IndexManagement.PostgreSqlIndexManager(Created());
        Exec("CREATE SCHEMA IF NOT EXISTS birko_other");
        Exec("DROP TABLE IF EXISTS birko_other.\"PgIdxDir\"");
        Exec("CREATE TABLE birko_other.\"PgIdxDir\" (x int)");
        Exec("CREATE INDEX \"IX_PgIdxDir_OtherOnly\" ON birko_other.\"PgIdxDir\" (x)");
        (await manager.ExistsAsync("IX_PgIdxDir_OtherOnly", "PgIdxDir")).Should().BeFalse();
        (await manager.ExistsAsync("IX_PgIdxDir_E", "PgIdxDir")).Should().BeTrue();
        (await manager.ListAsync("PgIdxDir")).Select(i => i.Name).Should().NotContain("IX_PgIdxDir_OtherOnly");
    }
}
