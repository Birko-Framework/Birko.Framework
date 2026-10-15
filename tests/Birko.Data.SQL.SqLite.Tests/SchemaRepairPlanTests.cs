using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SchemaDrift;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-633 on SQLite: <c>PlanRepair</c> writes the statements that repair drift, and lists what it leaves to an operator.
/// Measured by running exactly those statements as text and reading the table back. The server providers' twins are each
/// suite's <c>SchemaRepairPlanLiveTests</c>.
/// </summary>
public class SchemaRepairPlanTests : IDisposable
{
    private const string TableName = "Repair633";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-repair-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public enum Stage { Draft = 0, Live = 1 }

    [Table(TableName)]
    public class Before : AbstractModel
    {
        public string? Name { get; set; }
    }

    [Table(TableName)]
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

    [Table(TableName)]
    public class Renamed : AbstractModel
    {
        public string? Label { get; set; }
    }

    /// <summary>The table holds a column no model maps, and the model gains a column no default can fill.</summary>
    [Table(TableName)]
    public class Operators : AbstractModel
    {
        [RequiredField]
        public string? Code { get; set; }
    }

    private SqLiteConnector Connector()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "repair.db" });
        return (SqLiteConnector)factory.GetConnector();
    }

    /// <summary>
    /// Runs a statement on a connection of the test's own, as a consumer's deploy would. <paramref name="register"/> is what
    /// such a connection must do for a statement naming the decimal collation (rule 64; the plan's Requirements say so).
    /// </summary>
    private void Exec(string sql, bool register = true)
    {
        var settings = new SqLiteSettings { Location = _root, Name = "repair.db" };
        using var raw = new Microsoft.Data.Sqlite.SqliteConnection(settings.GetConnectionString());
        if (register) Birko.Data.SQL.Connectors.SqLiteDecimal.Register(raw);
        raw.Open();
        using var command = raw.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<T> Rows<T>(AbstractConnector connector)
        => connector.Select(typeof(T), (LambdaExpression?)null).Cast<T>().ToList();

    private (SqLiteConnector Connector, Guid Existing) OldTableWithARow()
    {
        var connector = Connector();
        connector.CreateTable(new[] { typeof(Before) });
        var existing = Guid.NewGuid();
        connector.Insert(new Before { Guid = existing, Name = "old" });
        return (connector, existing);
    }

    [Fact]
    public void The_plan_repairs_a_table_that_predates_its_columns_and_the_old_row_reads_back()
    {
        var (connector, existing) = OldTableWithARow();

        var plan = connector.PlanRepair(typeof(After));
        foreach (var s in plan.Statements) Exec(s);

        plan.Statements.Should().HaveCount(8);
        plan.Statements.Should().OnlyContain(s => s.StartsWith("ALTER TABLE \"Repair633\" ADD COLUMN "));
        plan.IsComplete.Should().BeTrue();
        plan.Requirements.Should().ContainSingle().Which.Should().Contain("SqLiteDecimal.Register");
        Rows<After>(connector).Single(x => x.Guid == existing).Should().BeEquivalentTo(new After { Guid = existing, Name = "old" },
            "the old row must read back as defaults, not as a NULL a non-nullable reader throws on");
        connector.DetectDrift(typeof(After)).Drifts.Should().BeEmpty();
        connector.PlanRepair(typeof(After)).Statements.Should().BeEmpty("a second plan finds nothing to repair");
    }

    /// <summary>
    /// Why the plan carries Requirements: the decimal column's ADD names a collation an unregistered connection (the
    /// sqlite3 CLI, a raw SqliteConnection) does not have, and it refuses the statement.
    /// </summary>
    [Fact]
    public void A_decimal_ADD_fails_on_a_connection_without_the_registration()
    {
        var (connector, _) = OldTableWithARow();
        var decimalAdd = connector.PlanRepair(typeof(After)).Statements.Single(s => s.Contains("Amount"));

        var act = () => Exec(decimalAdd, register: false);

        act.Should().Throw<Microsoft.Data.Sqlite.SqliteException>().WithMessage("*collation*");
    }

    [Fact]
    public void The_rename_statement_keeps_the_values()
    {
        var (connector, existing) = OldTableWithARow();

        Exec(connector.RenameColumnSql(TableName, connector.StoredColumnName("Name"), connector.StoredColumnName("Label")));

        Rows<Renamed>(connector).Single(x => x.Guid == existing).Label.Should().Be("old");
    }

    [Fact]
    public void Drifts_that_could_lose_data_are_listed_and_written_as_nothing()
    {
        var (connector, _) = OldTableWithARow();

        var plan = connector.PlanRepair(typeof(Operators));

        plan.Statements.Should().BeEmpty();
        plan.IsComplete.Should().BeFalse();
        plan.NotActioned.Select(s => s.Drift).Should().Contain(d => d.Contains("Code") && d.Contains("not present"),
            "a [Required] string has no value-type default, so any back-fill would be invented");
        plan.NotActioned.Select(s => s.Drift).Should().Contain(d => d.Contains("Name") && d.Contains("not declared"),
            "an unmapped column may hold data; dropping it is the operator's");
        plan.NotActioned.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.Reason));
    }

    [Fact]
    public void A_table_that_does_not_exist_yet_gets_an_empty_plan()
    {
        var plan = Connector().PlanRepair(typeof(After));

        plan.Statements.Should().BeEmpty();
        plan.Report.TableExists.Should().BeFalse();
    }

    [Fact]
    public void An_unmapped_type_is_refused()
    {
        var act = () => Connector().PlanRepair(typeof(string));

        act.Should().Throw<NotSupportedException>();
    }
}
