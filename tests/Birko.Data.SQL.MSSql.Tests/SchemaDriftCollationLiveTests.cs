using System;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SchemaDrift;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-611 — SQL Server resolves a column reference by the DATABASE collation. Under a case-sensitive one the bare
/// <c>SupplierVatin</c> the connector emits does not reach a column stored as <c>suppliervatin</c>, so the drift check
/// must match exactly there, and case-insensitively under the default collation (where it IS the same column).
/// <para>Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </summary>
public class SchemaDriftCollationLiveTests
{
    private const string CaseTable = "MsDriftCase";
    private const string CsDatabase = "birko611cs";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string DefaultDatabase => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";

    private readonly ITestOutputHelper _output;
    public SchemaDriftCollationLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings(string database) => new(Host!, database, User, Password, Port) { TrustServerCertificate = true };

    private static void Exec(string database, string sql)
    {
        using var conn = new SqlConnection(Settings(database).GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Table(CaseTable)]
    public class CaseRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }
        public int SupplierVatin { get; set; }
    }

    /// <summary>A framework-created table whose column was then renamed to differ only in case.</summary>
    private static MSSqlConnector CaseMismatchedTable(string database)
    {
        Exec(database, $"DROP TABLE IF EXISTS [{CaseTable}]");
        var connector = new MSSqlConnector(Settings(database));
        connector.CreateTable(new[] { typeof(CaseRow) });
        Exec(database, $"EXEC sp_rename '{CaseTable}.SupplierVatin', 'suppliervatin', 'COLUMN'");
        return connector;
    }

    [Fact]
    public void Under_a_case_sensitive_collation_a_column_differing_only_in_case_is_reported()
    {
        if (!RequireServer()) return;
        Exec("master", $"IF DB_ID('{CsDatabase}') IS NULL CREATE DATABASE [{CsDatabase}] COLLATE Latin1_General_CS_AS");

        var connector = CaseMismatchedTable(CsDatabase);

        // The premise, measured: no query of the framework reaches the column.
        var select = () => connector.Select(typeof(CaseRow), (System.Linq.Expressions.LambdaExpression?)null).Cast<object>().ToList();
        select.Should().Throw<Exception>().Where(e => e.ToString().Contains("Invalid column name"));

        var drift = connector.DetectDrift(typeof(CaseRow)).Drifts.Should().ContainSingle().Subject;
        drift.Kind.Should().Be(ColumnDriftKind.CaseMismatch);
        drift.StoredColumn.Should().Be("suppliervatin");
        connector.EnsureColumns(typeof(CaseRow)).Should().BeEmpty();
    }

    [Fact]
    public void Under_the_default_collation_the_same_rename_is_the_same_column_and_reports_clean()
    {
        if (!RequireServer()) return;

        var connector = CaseMismatchedTable(DefaultDatabase);

        connector.Select(typeof(CaseRow), (System.Linq.Expressions.LambdaExpression?)null).Cast<object>().ToList().Should().BeEmpty();
        connector.DetectDrift(typeof(CaseRow)).IsClean.Should().BeTrue();
    }
}
