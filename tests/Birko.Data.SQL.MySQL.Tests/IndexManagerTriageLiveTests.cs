using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-154 on MySQL: what the index manager reports for direction (SH-M232 / SH-M244) and for a same-named table
/// outside the connection's schema or database (SH-M227).
/// </summary>
/// <remarks>Gated on the provider's host variable; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</remarks>
public class IndexManagerTriageLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public IndexManagerTriageLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
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

    [Table("MyIdxDir")]
    public class Row : AbstractModel
    {
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
        public int D { get; set; }
        [IndexedField("IX_MyIdxDir_E", 0, true)] public int E { get; set; }
        [IndexedField("IX_MyIdxDir_Mix", 0)] public int F { get; set; }
        [IndexedField("IX_MyIdxDir_Mix", 1, true)] public int G { get; set; }
    }

    private MySQLConnector Created()
    {
        Exec("DROP TABLE IF EXISTS `MyIdxDir`");
        var connector = new MySQLConnector(Settings());
        connector.CreateTable(new[] { typeof(Row) });
        return connector;
    }

    [Fact]
    public async Task A_descending_column_is_listed_descending_wherever_it_sits()
    {
        if (!RequireServer()) return;
        var listed = (await new Birko.Data.SQL.MySQL.IndexManagement.MySqlIndexManager(Created()).ListAsync("MyIdxDir")).ToDictionary(i => i.Name);

        listed["IX_MyIdxDir_E"].Fields.Single().IsDescending.Should().BeTrue("E is the table's 6th column and the index's only one");
        listed["IX_MyIdxDir_Mix"].Fields.Select(f => (f.Name.ToUpperInvariant(), f.IsDescending)).Should().Equal(("F", false), ("G", true));
        listed.Keys.Should().NotContain("PRIMARY");
    }

    [Fact]
    public async Task A_same_named_table_elsewhere_does_not_answer_for_this_one()
    {
        if (!RequireServer()) return;
        var manager = new Birko.Data.SQL.MySQL.IndexManagement.MySqlIndexManager(Created());
        Exec("CREATE DATABASE IF NOT EXISTS birko_other");
        Exec("DROP TABLE IF EXISTS birko_other.`MyIdxDir`");
        Exec("CREATE TABLE birko_other.`MyIdxDir` (x int)");
        Exec("CREATE INDEX `IX_MyIdxDir_OtherOnly` ON birko_other.`MyIdxDir` (x)");
        (await manager.ExistsAsync("IX_MyIdxDir_OtherOnly", "MyIdxDir")).Should().BeFalse();
        (await manager.ExistsAsync("IX_MyIdxDir_E", "MyIdxDir")).Should().BeTrue();
        (await manager.ListAsync("MyIdxDir")).Select(i => i.Name).Should().NotContain("IX_MyIdxDir_OtherOnly");
    }
}
