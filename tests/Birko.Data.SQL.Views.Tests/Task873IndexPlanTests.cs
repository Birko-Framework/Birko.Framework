using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-873 — an exact string equality on an indexed column is still answered by the index: the plan of
/// <c>x.Code == "abc"</c> and of <c>codes.Contains(x.Code)</c> seeks (SQL Server), looks up or range-scans (MySQL), uses an
/// index scan with sequential scans disabled (PostgreSQL) or searches the index (SQLite). An operand under a binary
/// collation alone is no longer the indexed column: measured, <c>Code COLLATE Latin1_General_100_BIN2 = @p</c> scanned the
/// whole index on SQL Server, <c>Code COLLATE utf8mb4_0900_bin = …</c> on MySQL, and <c>Code COLLATE "C" = $1</c> was a
/// sequential scan on PostgreSQL. The exact form keeps the plain comparison in front for that reason.
/// </summary>
[Collection("T873P")]
public abstract class Task873IndexPlanTestsBase : IDisposable
{
    protected readonly ITestOutputHelper Output;
    protected Task873IndexPlanTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    /// <summary>The statement whose plan is read: the SELECT wrapped as this provider explains it.</summary>
    protected abstract string Explain(string select);

    /// <summary>True when the plan text uses the index on Code to find the rows (not a scan of every row or entry).</summary>
    protected abstract bool UsesIndex(string plan);

    [Table("T873PlanRows")]
    public class T873PlanRow : AbstractModel
    {
        [MaxLengthField(64)]
        [IndexedField("IX_T873PlanRows_Code")]
        public string Code { get; set; } = string.Empty;

        public int Hits { get; set; }
    }

    private static readonly string[] Codes = { "abc", "ß" };

    public static readonly IReadOnlyDictionary<string, Expression<Func<T873PlanRow, bool>>> Shapes =
        new Dictionary<string, Expression<Func<T873PlanRow, bool>>>
        {
            ["Code == \"abc\""] = x => x.Code == "abc",
            ["{abc, ß}.Contains(Code)"] = x => Codes.Contains(x.Code),
            ["Code == \"abc\" && Hits >= 0"] = x => x.Code == "abc" && x.Hits >= 0,
        };

    public static IEnumerable<object[]> ShapeNames => Shapes.Keys.Select(k => new object[] { k });

    private AbstractConnector? _connector;

    private bool Live()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private AbstractConnector? Seeded()
    {
        if (!Live()) return null;
        var connector = Connector();
        _connector = connector;
        try { connector.DropTable(new[] { typeof(T873PlanRow) }); } catch { }
        connector.CreateTable(new[] { typeof(T873PlanRow) });
        var table = DataBase.LoadTable(typeof(T873PlanRow));
        var rows = Enumerable.Range(0, 3000).Select(i => (object)new T873PlanRow { Guid = Guid.NewGuid(), Code = "C" + i, Hits = i % 7 })
            .Concat(new[] { "abc", "ABC", "abc ", "ß", "ss" }.Select(c => (object)new T873PlanRow { Guid = Guid.NewGuid(), Code = c }))
            .ToList();
        connector.Insert(table, rows);
        Analyze(connector);
        return connector;
    }

    /// <summary>Refreshes the optimizer's statistics after the seed, where the provider keeps them.</summary>
    protected virtual void Analyze(AbstractConnector connector) { }

    protected static void Run(AbstractConnector connector, string sql)
        => connector.DoCommand(command => command.CommandText = sql, command => command.ExecuteNonQuery());

    private string Plan(AbstractConnector connector, Expression<Func<T873PlanRow, bool>> filter)
    {
        var plan = new StringBuilder();
        connector.DoCommand(command =>
        {
            var where = connector.ConditionDefinition(DataBase.ParseConditionExpression(filter), command);
            var select = $"SELECT Code FROM {connector.TableIdentifier("T873PlanRows")} T873PlanRows WHERE {where}";
            Output.WriteLine(select);
            command.CommandText = Explain(select);
        }, command =>
        {
            using var reader = command.ExecuteReader();
            do
            {
                while (reader.Read())
                    for (var i = 0; i < reader.FieldCount; i++)
                        if (!reader.IsDBNull(i) && reader.GetValue(i) is string text) plan.AppendLine(text);
            } while (reader.NextResult());
        });
        Output.WriteLine(plan.ToString());
        return plan.ToString();
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void An_exact_string_equality_on_an_indexed_column_is_answered_by_the_index(string shape)
    {
        var connector = Seeded();
        if (connector == null) return;
        UsesIndex(Plan(connector, Shapes[shape])).Should().BeTrue($"'{shape}' must still use IX_T873PlanRows_Code");
    }

    public void Dispose()
    {
        if (_connector == null) return;
        try { _connector.DropTable(new[] { typeof(T873PlanRow) }); } catch { }
    }
}

public sealed class SqliteTask873IndexPlanTests : Task873IndexPlanTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t873p-{Guid.NewGuid():N}.db");
    public SqliteTask873IndexPlanTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => Task873Connectors.Sqlite(_path);
    protected override string Explain(string select) => "EXPLAIN QUERY PLAN " + select;
    protected override void Analyze(AbstractConnector connector) => Run(connector, "ANALYZE");

    /// <summary><c>SEARCH … USING [COVERING] INDEX IX_T873PlanRows_Code (Code=?)</c>; a scan reads <c>SCAN</c>.</summary>
    protected override bool UsesIndex(string plan)
        => plan.Contains("SEARCH", StringComparison.Ordinal) && plan.Contains("IX_T873PlanRows_Code", StringComparison.Ordinal);
}

public sealed class PostgreSqlTask873IndexPlanTests : Task873IndexPlanTestsBase
{
    public PostgreSqlTask873IndexPlanTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => Task873Connectors.PostgreSql();

    /// <summary>With sequential scans off, a predicate the index can answer is planned on it; one it cannot stays a Seq Scan.</summary>
    protected override string Explain(string select) => "SET enable_seqscan = off; EXPLAIN " + select;
    protected override void Analyze(AbstractConnector connector) => Run(connector, "ANALYZE \"T873PlanRows\"");

    /// <summary><c>Index [Only] Scan using … on …</c> or <c>Bitmap Index Scan on …</c>, with the index's condition.</summary>
    protected override bool UsesIndex(string plan)
        => plan.Contains("Index Cond", StringComparison.Ordinal) && !plan.Contains("Seq Scan", StringComparison.Ordinal);
}

/// <summary>
/// TASK-873 — on PostgreSQL a column the framework creates has the database's deterministic collation, whose <c>=</c> is
/// already exact; a column declared elsewhere with a nondeterministic ICU collation is not (measured: <c>Ci = $1</c> with the
/// <c>text</c> parameter <c>'abc'</c> matched <c>ABC</c>). A <c>citext</c> column compared with Npgsql's <c>text</c> parameter
/// was already exact (<c>citext = text</c> resolves to <c>text = text</c>); it is kept here as a guard. The exact term casts
/// to <c>text</c> under <c>COLLATE "C"</c>, so a filter over such a table matches what C# matches.
/// </summary>
public sealed class PostgreSqlTask873NondeterministicCollationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private AbstractConnector? _connector;
    public PostgreSqlTask873NondeterministicCollationTests(ITestOutputHelper output) => _output = output;

    [Table("T873CiRows")]
    public class T873CiRow : AbstractModel
    {
        public string? Ci { get; set; }
        public string? Cit { get; set; }
    }

    private static readonly string[] Values = { "abc", "ABC", "Abc", "abc ", "ß", "ss" };

    private static void Run(AbstractConnector connector, string sql)
        => connector.DoCommand(command => command.CommandText = sql, command => command.ExecuteNonQuery());

    private static long Count(AbstractConnector connector, Expression<Func<T873CiRow, bool>> filter)
    {
        long count = 0;
        connector.DoCommand(command =>
        {
            var where = connector.ConditionDefinition(DataBase.ParseConditionExpression(filter), command);
            command.CommandText = $"SELECT COUNT(*) FROM {connector.TableIdentifier("T873CiRows")} T873CiRows WHERE {where}";
        }, command => count = Convert.ToInt64(command.ExecuteScalar()));
        return count;
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ss")]
    [InlineData("ABC")]
    public void A_column_with_a_nondeterministic_collation_or_citext_compares_as_CSharp_does(string value)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")))
        {
            _output.WriteLine("SKIPPED: set BIRKO_PG_HOST to run these live tests.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("SKIPPED: set BIRKO_PG_HOST to run these live tests.");
            return;
        }
        var connector = Task873Connectors.PostgreSql();
        _connector = connector;
        Run(connector, "DROP TABLE IF EXISTS \"T873CiRows\"");
        Run(connector, "DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_collation WHERE collname = 't873_ci') THEN "
            + "CREATE COLLATION t873_ci (provider = icu, locale = 'und-u-ks-level2', deterministic = false); END IF; END $$");
        Run(connector, "CREATE EXTENSION IF NOT EXISTS citext");
        Run(connector, "CREATE TABLE \"T873CiRows\" (Guid UUID PRIMARY KEY, Ci TEXT COLLATE t873_ci NULL, Cit CITEXT NULL)");
        foreach (var text in Values)
            Run(connector, $"INSERT INTO \"T873CiRows\" (Guid, Ci, Cit) VALUES ('{Guid.NewGuid()}', '{text}', '{text}')");

        var expected = Values.Count(v => v == value);
        Count(connector, x => x.Ci == value).Should().Be(expected, $"Ci == \"{value}\" must match what C# matches");
        Count(connector, x => x.Cit == value).Should().Be(expected, $"Cit == \"{value}\" must match what C# matches");
        Count(connector, x => x.Ci != value).Should().Be(Values.Length - expected, $"Ci != \"{value}\"");
        Count(connector, x => new[] { value, "zzz" }.Contains(x.Cit)).Should().Be(expected, $"{{{value}}}.Contains(Cit)");
    }

    public void Dispose()
    {
        if (_connector == null) return;
        try { Run(_connector, "DROP TABLE IF EXISTS \"T873CiRows\""); } catch { }
    }
}

/// <summary>
/// TASK-873 — on SQLite a column the framework creates is <c>BINARY</c>, already exact; a column declared <c>NOCASE</c> or
/// <c>RTRIM</c> elsewhere is not (measured: <c>= 'abc'</c> matched <c>ABC</c> / <c>abc </c>). The exact term names
/// <c>COLLATE BINARY</c>, so a filter over such a table matches what C# matches too.
/// </summary>
public sealed class SqliteTask873DeclaredCollationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t873c-{Guid.NewGuid():N}.db");

    [Table("T873NoCaseRows")]
    public class T873NoCaseRow : AbstractModel
    {
        public string? Nc { get; set; }
        public string? Rt { get; set; }
    }

    private static readonly string[] Values = { "abc", "ABC", "Abc", "abc ", "abc  " };

    private static void Run(AbstractConnector connector, string sql)
        => connector.DoCommand(command => command.CommandText = sql, command => command.ExecuteNonQuery());

    private static long Count(AbstractConnector connector, Expression<Func<T873NoCaseRow, bool>> filter)
    {
        long count = 0;
        connector.DoCommand(command =>
        {
            var where = connector.ConditionDefinition(DataBase.ParseConditionExpression(filter), command);
            command.CommandText = $"SELECT COUNT(*) FROM {connector.TableIdentifier("T873NoCaseRows")} T873NoCaseRows WHERE {where}";
        }, command => count = Convert.ToInt64(command.ExecuteScalar()));
        return count;
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ABC")]
    [InlineData("abc ")]
    public void A_column_declared_NOCASE_or_RTRIM_compares_as_CSharp_does(string value)
    {
        var connector = Task873Connectors.Sqlite(_path);
        Run(connector, "DROP TABLE IF EXISTS \"T873NoCaseRows\"");
        Run(connector, "CREATE TABLE \"T873NoCaseRows\" (Guid TEXT PRIMARY KEY, Nc TEXT COLLATE NOCASE, Rt TEXT COLLATE RTRIM)");
        foreach (var text in Values)
            Run(connector, $"INSERT INTO \"T873NoCaseRows\" (Guid, Nc, Rt) VALUES ('{Guid.NewGuid()}', '{text}', '{text}')");

        var expected = Values.Count(v => v == value);
        Count(connector, x => x.Nc == value).Should().Be(expected, $"Nc == \"{value}\" must match what C# matches");
        Count(connector, x => x.Rt == value).Should().Be(expected, $"Rt == \"{value}\" must match what C# matches");
        Count(connector, x => x.Nc != value).Should().Be(Values.Length - expected, $"Nc != \"{value}\"");
        Count(connector, x => new[] { value, "zzz" }.Contains(x.Rt)).Should().Be(expected, $"{{{value}}}.Contains(Rt)");
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch { }
    }
}

public sealed class MySqlTask873IndexPlanTests : Task873IndexPlanTestsBase
{
    public MySqlTask873IndexPlanTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => Task873Connectors.MySql();
    protected override string Explain(string select) => "EXPLAIN FORMAT=TREE " + select;
    protected override void Analyze(AbstractConnector connector) => Run(connector, "ANALYZE TABLE `T873PlanRows`");

    /// <summary><c>Index lookup</c> (one value) or <c>Index range scan … over (Code = …)</c> (a list); a full index scan reads <c>index scan</c>.</summary>
    protected override bool UsesIndex(string plan)
        => plan.Contains("index lookup", StringComparison.OrdinalIgnoreCase) || plan.Contains("index range scan", StringComparison.OrdinalIgnoreCase);
}

public sealed class MSSqlTask873IndexPlanTests : Task873IndexPlanTestsBase
{
    public MSSqlTask873IndexPlanTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => Task873Connectors.MSSql();

    /// <summary>The actual plan, as XML, after the rows.</summary>
    protected override string Explain(string select) => "SET STATISTICS XML ON; " + select + "; SET STATISTICS XML OFF;";
    protected override void Analyze(AbstractConnector connector) => Run(connector, "UPDATE STATISTICS [T873PlanRows] WITH FULLSCAN");

    /// <summary>An <c>Index Seek</c> on the index; a scan of it reads <c>Index Scan</c>.</summary>
    protected override bool UsesIndex(string plan)
        => plan.Contains("PhysicalOp=\"Index Seek\"", StringComparison.Ordinal)
            && !plan.Contains("PhysicalOp=\"Index Scan\"", StringComparison.Ordinal)
            && !plan.Contains("PhysicalOp=\"Clustered Index Scan\"", StringComparison.Ordinal)
            && !plan.Contains("PhysicalOp=\"Table Scan\"", StringComparison.Ordinal);
}
