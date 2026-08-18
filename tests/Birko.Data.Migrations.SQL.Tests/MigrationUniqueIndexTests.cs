using System;
using System.Data.Common;
using System.IO;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// TASK-246 — a migration's <c>.Unique()</c> built a <b>non-unique</b> index on every SQL provider.
///
/// <para>
/// <c>SqlIndexBuilder.Build()</c> has two paths. Its <b>connector path</b> — taken whenever a connector was
/// supplied, which is every production migration — constructed
/// <c>new Tables.IndexDefinition { Name = _indexName }</c> and never copied <c>_unique</c> onto it.
/// <c>IndexDefinition.Unique</c> defaults to <c>false</c> and <c>CreateIndexSql</c> emits <c>UNIQUE</c> only
/// when it is true, so the declared constraint was simply absent and duplicate rows the migration was
/// written to forbid were accepted from that point on. A missing <b>constraint</b>, not a missing
/// optimisation.
/// </para>
///
/// <para>
/// <b>What hid it — and why this file exists rather than an addition to <c>SqlSchemaBuilderTests</c>.</b>
/// The raw-SQL fallback three lines below the defect <i>does</i> honour <c>_unique</c>, and that fallback is
/// taken exactly when <c>connector == null</c> — which is how every pre-existing test in this project
/// constructs the builder (<c>new SqlSchemaBuilder(conn, null, null)</c>). So the feature was demonstrably
/// working in the path nobody uses in production and broken in the path everybody uses, and the suite could
/// not tell. Every test here therefore supplies a <b>real connector</b>.
/// </para>
///
/// <para>
/// The primary assertion is <b>enforcement</b>: insert the duplicate and require the write to fail. Whether
/// the word <c>UNIQUE</c> appears in <c>sqlite_master</c> is the weaker companion claim — for a unique index
/// the constraint is the entire point, and asserting the DDL text would pass against an index the engine
/// never applied.
/// </para>
/// </summary>
public class MigrationUniqueIndexTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public MigrationUniqueIndexTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"birko-mig-ux-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "mig-ux.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private AbstractConnector Connector()
        => DataBase.GetConnector<SqLiteConnector>(new SqLiteSettings(_dir, Path.GetFileName(_dbPath)));

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private static void Exec(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void CreateOrdersTable(DbConnection conn)
        => Exec(conn, "CREATE TABLE IF NOT EXISTS Orders (TenantGuid TEXT, Number TEXT, Status TEXT)");

    private static void Insert(DbConnection conn, string tenant, string number, string status = "open")
        => Exec(conn, $"INSERT INTO Orders (TenantGuid, Number, Status) VALUES ('{tenant}', '{number}', '{status}')");

    /// <summary>The index's DDL as SQLite recorded it — the companion assertion, never the primary one.</summary>
    private static string IndexSql(DbConnection conn, string indexName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(sql, '') FROM sqlite_master WHERE type='index' AND name=@n";
        var p = cmd.CreateParameter(); p.ParameterName = "@n"; p.Value = indexName;
        cmd.Parameters.Add(p);
        return (string?)cmd.ExecuteScalar() ?? "";
    }

    // ---------------------------------------------------------------- the defect

    [Fact]
    public void A_migration_declared_unique_index_enforces_the_constraint()
    {
        var connector = Connector();
        using var conn = OpenConnection();
        CreateOrdersTable(conn);

        new SqlSchemaBuilder(conn, null, connector)
            .CreateIndex("Orders", "ux_order_docnum")
            .WithField("TenantGuid")
            .WithField("Number")
            .Unique()
            .Build();

        var tenant = Guid.NewGuid().ToString();
        Insert(conn, tenant, "FV2026000001");

        // THE assertion: the engine must reject the duplicate. Against the unfixed builder the index was
        // created without UNIQUE, so this insert succeeded and the declared constraint did not exist.
        Action duplicate = () => Insert(conn, tenant, "FV2026000001");
        duplicate.Should().Throw<SqliteException>(
            "the migration declared .Unique(), so a second (TenantGuid, Number) pair must be refused");

        // …and uniqueness is per the declared pair, not global.
        Action otherTenant = () => Insert(conn, Guid.NewGuid().ToString(), "FV2026000001");
        otherTenant.Should().NotThrow("the same Number under a different tenant is a different key");
    }

    [Fact]
    public void The_emitted_ddl_records_the_index_as_unique()
    {
        var connector = Connector();
        using var conn = OpenConnection();
        CreateOrdersTable(conn);

        new SqlSchemaBuilder(conn, null, connector)
            .CreateIndex("Orders", "ux_order_docnum")
            .WithField("TenantGuid").WithField("Number")
            .Unique()
            .Build();

        IndexSql(conn, "ux_order_docnum").Should().Contain("UNIQUE",
            "the weaker companion to the enforcement test above — stated second deliberately, because DDL "
          + "text would also 'pass' for an index the engine never applied");
    }

    // ---------------------------------------------------------------- what must NOT change

    /// <summary>
    /// The guard against "fixing" this by making every migration index unique. A builder that never called
    /// <c>.Unique()</c> must still produce a plain index, and duplicates must still be accepted.
    /// </summary>
    [Fact]
    public void A_migration_index_without_unique_stays_non_unique()
    {
        var connector = Connector();
        using var conn = OpenConnection();
        CreateOrdersTable(conn);

        new SqlSchemaBuilder(conn, null, connector)
            .CreateIndex("Orders", "ix_order_status")
            .WithField("Status")
            .Build();

        Insert(conn, Guid.NewGuid().ToString(), "A", "open");
        Action duplicate = () => Insert(conn, Guid.NewGuid().ToString(), "B", "open");

        duplicate.Should().NotThrow("no .Unique() was declared, so duplicates are legitimate");
        IndexSql(conn, "ix_order_status").Should().NotContain("UNIQUE");
    }

    /// <summary>
    /// <c>Unique</c> was not the only thing set in that object initialiser — column order and the descending
    /// flag are populated in the same expression and had no test either, so a fix that touched it could have
    /// broken them silently.
    /// </summary>
    [Fact]
    public void Column_order_and_the_descending_flag_survive_the_hand_off()
    {
        var connector = Connector();
        using var conn = OpenConnection();
        CreateOrdersTable(conn);

        new SqlSchemaBuilder(conn, null, connector)
            .CreateIndex("Orders", "ix_order_desc")
            .WithField("Number", descending: true)
            .WithField("Status")
            .Build();

        var sql = IndexSql(conn, "ix_order_desc");

        sql.Should().Contain("DESC", "the descending flag is set in the same initialiser as Unique");
        sql.IndexOf("Number", StringComparison.Ordinal)
           .Should().BeLessThan(sql.IndexOf("Status", StringComparison.Ordinal),
               "and declaration order is the key order");
    }

    /// <summary>
    /// The fallback path is out of scope for the fix (it always honoured <c>_unique</c>) but is pinned here,
    /// because it is the reason the defect was invisible: every pre-existing test in this project builds with
    /// <c>connector: null</c> and therefore exercised only this branch.
    /// </summary>
    [Fact]
    public void The_no_connector_fallback_was_already_correct_and_stays_correct()
    {
        using var conn = OpenConnection();
        CreateOrdersTable(conn);

        new SqlSchemaBuilder(conn, null, null)
            .CreateIndex("Orders", "ux_fallback")
            .WithField("TenantGuid").WithField("Number")
            .Unique()
            .Build();

        IndexSql(conn, "ux_fallback").Should().Contain("UNIQUE");

        var tenant = Guid.NewGuid().ToString();
        Insert(conn, tenant, "FV1");
        Action duplicate = () => Insert(conn, tenant, "FV1");
        duplicate.Should().Throw<SqliteException>();
    }
}
