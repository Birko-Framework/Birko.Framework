using System;
using Birko.Data.SQL.SchemaDrift;
using System.Linq.Expressions;
using System.Linq;
using System.Collections.Generic;
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
/// TASK-633 on MySQL: <c>PlanRepair</c> turns drift into statements a host runs itself. Measured by running exactly
/// those statements as text, then reading the table back through the connector.
/// </summary>
/// <remarks>Gated on the provider's host variable; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</remarks>
public class SchemaRepairPlanLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public SchemaRepairPlanLiveTests(ITestOutputHelper output) => _output = output;

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

    public enum Stage { Draft = 0, Live = 1 }

    [Table("MyRepair633")]
    public class Before : AbstractModel
    {
        public string? Name { get; set; }
    }

    [Table("MyRepair633")]
    public class After : AbstractModel
    {
        public string? Name { get; set; }
        public bool Flag { get; set; }
        public int Count { get; set; }
        public decimal Amount { get; set; }
        public Guid Reference { get; set; }
        public DateTime Seen { get; set; }
        public Stage State { get; set; }
        public int? Optional { get; set; }
        public string? Note { get; set; }
    }

    [Table("MyRepair633")]
    public class Renamed : AbstractModel
    {
        public string? Label { get; set; }
    }

    private static List<T> Rows<T>(AbstractConnector connector)
        => connector.Select(typeof(T), (LambdaExpression?)null).Cast<T>().ToList();

    private static (MySQLConnector Connector, Guid Existing) OldTableWithARow()
    {
        var connector = new MySQLConnector(Settings());
        connector.DropTable(new[] { typeof(Before) });
        connector.CreateTable(new[] { typeof(Before) });
        var existing = Guid.NewGuid();
        connector.Insert(new Before { Guid = existing, Name = "old" });
        return (connector, existing);
    }

    [Fact]
    public void The_plan_repairs_a_table_that_predates_its_columns_and_the_old_row_reads_back()
    {
        if (!RequireServer()) return;
        var (connector, existing) = OldTableWithARow();

        var plan = connector.PlanRepair(typeof(After));
        foreach (var s in plan.Statements) _output.WriteLine(s);
        foreach (var s in plan.Statements) Exec(s);

        plan.Statements.Should().HaveCount(8);
        plan.IsComplete.Should().BeTrue();
        Rows<After>(connector).Single(x => x.Guid == existing).Should().BeEquivalentTo(new After { Guid = existing, Name = "old" },
            "the old row reads back as an entity that never assigned the new properties, not as a NULL a typed reader throws on");
        connector.DetectDrift(typeof(After)).Drifts.Should().BeEmpty("a re-run of the detection finds nothing left");
        connector.PlanRepair(typeof(After)).Statements.Should().BeEmpty();
    }

    [Fact]
    public void The_rename_statement_keeps_the_values()
    {
        if (!RequireServer()) return;
        var (connector, existing) = OldTableWithARow();

        Exec(connector.RenameColumnSql("MyRepair633", connector.StoredColumnName("Name"), connector.StoredColumnName("Label")));

        Rows<Renamed>(connector).Single(x => x.Guid == existing).Label.Should().Be("old");
    }
}
