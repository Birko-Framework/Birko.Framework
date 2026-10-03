using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SchemaDrift;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-510 — <c>EnsureColumns</c> on SQL Server, against a live server: a table created before its model grew
/// columns, holding a row, gains every missing column, and the old row reads back as <c>default(T)</c>.
///
/// <para>
/// What only a server can answer is whether each <c>DEFAULT</c> literal is one it accepts for the column type
/// <c>ConvertType</c> emits, and whether the value it back-fills reads back through the field's own reader.
/// Here <c>ADD COLUMN</c> itself was a syntax error (Msg 156) until this task, so this suite is also the first proof that <c>AlterTableAdd</c> works on SQL Server at all. The SQLite twin is <c>Birko.Data.SQL.SqLite.Tests.EnsureColumnsEndToEndTests</c>.
/// </para>
///
/// <para>Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public class EnsureColumnsLiveTests
{
    private const string TableName = "MsEnsureColumns";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public EnsureColumnsLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port) { TrustServerCertificate = true };

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
        public long Total { get; set; }
        public short Small { get; set; }
        public double Cost { get; set; }
        public float Weight { get; set; }
        public decimal Amount { get; set; }
        [PrecisionField(18)]
        [ScaleField(2)]
        public decimal Price { get; set; }
        public Guid Reference { get; set; }
        public DateTime Seen { get; set; }
        [UtcField]
        public DateTime SeenUtc { get; set; }
        public TimeOnly Opens { get; set; }
        public Stage State { get; set; }
        public int? Optional { get; set; }
        [RequiredField]
        public int? RequiredNullable { get; set; }
        public string? Note { get; set; }
    }

    private static List<T> Rows<T>(AbstractConnector connector)
        => connector.Select(typeof(T), (LambdaExpression?)null).Cast<T>().ToList();

    private static (MSSqlConnector Connector, Guid Existing) OldTableWithARow()
    {
        var connector = new MSSqlConnector(Settings());
        connector.DropTable(new[] { typeof(Before) });
        connector.CreateTable(new[] { typeof(Before) });
        var existing = Guid.NewGuid();
        connector.Insert(new Before { Guid = existing, Name = "old" });
        return (connector, existing);
    }

    [Fact]
    public void An_old_table_with_a_row_gains_every_missing_column_and_the_row_reads_back_as_defaults()
    {
        if (!RequireServer()) return;
        var (connector, existing) = OldTableWithARow();

        var added = connector.EnsureColumns(typeof(After));

        foreach (var d in added) _output.WriteLine(d.ToString());
        added.Should().HaveCount(16);
        added.Should().OnlyContain(d => d.Kind == ColumnDriftKind.Missing);

        var row = Rows<After>(connector).Single(x => x.Guid == existing);
        // RequiredNullable: [RequiredField] makes the column NOT NULL, so the back-fill is the underlying default.
        row.Should().BeEquivalentTo(new After { Guid = existing, Name = "old", RequiredNullable = 0 },
            "an old row must read back exactly as an entity that never assigned the new properties");
    }

    /// <summary>
    /// The upgraded table must be indistinguishable from one <c>CREATE TABLE</c> made — asserted as "same
    /// drift report", not "clean", because <c>DetectDrift</c> has its own false positive for an unprecisioned
    /// <c>decimal</c> on MySQL and SQL Server (the server stores its default precision; TASK-511) and that
    /// must not be mistaken for something this call did.
    /// </summary>
    [Fact]
    public void An_ensured_table_reports_exactly_what_a_table_created_whole_reports()
    {
        if (!RequireServer()) return;
        var (connector, _) = OldTableWithARow();
        connector.EnsureColumns(typeof(After));
        var ensured = connector.DetectDrift(typeof(After)).Drifts.Select(d => d.ToString()).ToList();

        connector.DropTable(new[] { typeof(After) });
        connector.CreateTable(new[] { typeof(After) });
        var created = connector.DetectDrift(typeof(After)).Drifts.Select(d => d.ToString()).ToList();

        foreach (var d in ensured) _output.WriteLine("ensured: " + d);
        foreach (var d in created) _output.WriteLine("created: " + d);
        ensured.Should().BeEquivalentTo(created);
        ensured.Should().NotContain(d => d.Contains("not present"), "nothing the model declares may still be missing");
    }

    [Fact]
    public void After_ensuring_the_table_takes_new_rows_and_a_second_run_adds_nothing()
    {
        if (!RequireServer()) return;
        var (connector, _) = OldTableWithARow();
        connector.EnsureColumns(typeof(After));

        var fresh = new After
        {
            Guid = Guid.NewGuid(), Name = "new", Flag = true, Count = 3, Total = 4, Small = 5, Cost = 1.5,
            Weight = 2.5f, Amount = 7m, Price = 9.25m, Reference = Guid.NewGuid(),
            Seen = new DateTime(2026, 10, 3, 12, 30, 0), SeenUtc = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc),
            Opens = new TimeOnly(8, 0), State = Stage.Live, Optional = 11, RequiredNullable = 12, Note = "n",
        };
        connector.Insert(fresh);
        Rows<After>(connector).Single(x => x.Guid == fresh.Guid).Should().BeEquivalentTo(fresh);

        connector.EnsureColumns(typeof(After)).Should().BeEmpty();
    }
}
