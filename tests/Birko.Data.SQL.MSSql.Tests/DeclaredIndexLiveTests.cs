using System;
using System.Collections.Generic;
using System.Threading;
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
/// TASK-245 — MSSql was <b>not</b> broken (it already overrode <c>CreateIndexSql</c> with a
/// <c>sys.indexes</c> guard, which is why the MySQL defect never showed here), but that task changed two
/// things on this provider and both need pinning on a live server:
///
/// <list type="number">
/// <item><c>CreateIndexSql</c> gained a <c>conditional</c> parameter, so that
/// <c>CreateIndexes(..., throwIfExists: true)</c> means the same thing here as everywhere else instead of
/// being silently ignored on the providers whose conditional DDL cannot raise.</item>
/// <item><c>MSSqlIndexManager.CreateUniqueIndexSql</c> was <b>deleted</b> as byte-equivalent to what
/// <c>MSSqlConnector.CreateIndexSql</c> already emits once the <c>Unique</c> flag survives the hand-off.
/// "Byte-equivalent" was a reading of two methods; this is the measurement of it.</item>
/// </list>
///
/// <para>
/// The offline half lives in <c>MSSqlIndexDdlTests</c>. This suite is gated on <c>BIRKO_MSSQL_HOST</c>.
/// </para>
///
/// <para>
/// <b>Its probe entity declares <c>[MaxLengthField]</c> on both indexed columns deliberately, and that is
/// no longer the only shape covered.</b> This suite is about index DDL mechanics, so a bounded column keeps
/// the variable under test to one thing. TASK-257 later found that an <i>unlengthed</i> indexed string could
/// never be indexed here at all — the column was <c>TEXT</c>, which SQL Server refuses as an index key
/// (Msg 1919) — and that case has its own suite,
/// <see cref="IndexOverUnlengthedStringLiveTests"/>. Do not "fix" the lengths here to cover it; the two
/// suites test different things.
/// </para>
/// </summary>
public class DeclaredIndexLiveTests : IDisposable
{
    private const string TableName = "MsIdxRows";
    private const string UniqueIndex = "ux_msidxrows_docnum";
    private const string PlainIndex = "ix_msidxrows_status";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public DeclaredIndexLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            return true;
        }
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port)
    {
        TrustServerCertificate = true
    };

    /// <remarks>
    /// <c>AbstractDatabaseLogModel</c>, not the plain <c>AbstractLogModel</c> the MySQL and PostgreSQL twins
    /// use: it is the SQL-specific base that initialises <c>CreatedAt</c>/<c>UpdatedAt</c> to
    /// <c>DateTime.UtcNow</c>. Left at <c>default(DateTime)</c> those are 0001-01-01, which SQL Server's
    /// <c>datetime</c> cannot store at all (its floor is 1753) — MySQL and PostgreSQL accept it, so the
    /// wrong base only fails here.
    /// </remarks>
    [Table(TableName)]
    [CompositeIndex(UniqueIndex, nameof(TenantGuid), nameof(Number), IsUnique = true)]
    [CompositeIndex(PlainIndex, nameof(Status), nameof(Number))]
    public class MsIdxRow : AbstractDatabaseLogModel
    {
        public Guid TenantGuid { get; set; }

        [MaxLengthField(64)]
        public string Number { get; set; } = null!;

        [MaxLengthField(32)]
        public string Status { get; set; } = null!;
    }

    private static void Exec(string sql)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS [{TableName}]"); } catch { }
    }

    private static MSSqlConnector NewConnector() => new(Settings());

    private static List<string> IndexColumns(string index)
    {
        var result = new List<string>();
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT c.name
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
WHERE i.name = @i AND i.object_id = OBJECT_ID(@t) AND ic.is_included_column = 0
ORDER BY ic.key_ordinal";
        cmd.Parameters.AddWithValue("@i", index);
        cmd.Parameters.AddWithValue("@t", TableName);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static bool IsUnique(string index)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_unique FROM sys.indexes WHERE name = @i AND object_id = OBJECT_ID(@t)";
        cmd.Parameters.AddWithValue("@i", index);
        cmd.Parameters.AddWithValue("@t", TableName);
        var value = cmd.ExecuteScalar();
        return value is bool b && b;
    }

    [Fact]
    public void Declared_indexes_are_created_on_mssql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{TableName}]");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(MsIdxRow) });

        IndexColumns(PlainIndex).Should().Equal(new[] { "Status", "Number" },
            "MSSql was never broken by the IF NOT EXISTS defect — this is the no-regression pin for the "
          + "conditional-parameter change");
        IndexColumns(UniqueIndex).Should().Equal(new[] { "TenantGuid", "Number" });
        IsUnique(UniqueIndex).Should().BeTrue();
        IsUnique(PlainIndex).Should().BeFalse();
        connector.IndexCreationFailures.Should().BeEmpty();
    }

    /// <summary>
    /// The <c>sys.indexes</c> guard still makes a second schema-ensure a no-op, and the client-side
    /// tolerance added for MySQL must never engage here.
    /// </summary>
    [Fact]
    public void A_second_schema_ensure_is_a_no_op_on_mssql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{TableName}]");

        var connector = NewConnector();
        var raised = new List<IndexCreationFailure>();
        connector.OnIndexCreationFailed += raised.Add;

        connector.CreateTable(new[] { typeof(MsIdxRow) });
        connector.CreateTable(new[] { typeof(MsIdxRow) });

        raised.Should().BeEmpty();
        connector.IndexCreationFailures.Should().BeEmpty();
        IndexColumns(UniqueIndex).Should().HaveCount(2, "and no duplicate index");

        connector.IsIndexAlreadyExistsException(new Exception("anything")).Should().BeFalse(
            "MSSql synthesises its own guard, so the condition never reaches the client");
    }

    /// <summary>
    /// <c>throwIfExists: true</c> drops the <c>sys.indexes</c> guard, so an already-present index raises —
    /// the same meaning the flag has on MySQL and PostgreSQL rather than a silent no-op here.
    /// </summary>
    [Fact]
    public void CreateIndexes_with_throwIfExists_raises_for_an_already_present_index()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{TableName}]");
        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(MsIdxRow) });

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(MsIdxRow));
        var index = table!.Indexes![PlainIndex];

        connector.Invoking(c => c.CreateIndexes(TableName, new[] { index }, throwIfExists: true))
                 .Should().Throw<Exception>("without the guard SQL Server reports 'already exists'");

        connector.Invoking(c => c.CreateIndexes(TableName, new[] { index }))
                 .Should().NotThrow("and the default stays an ensure");
    }

    /// <summary>
    /// The deleted <c>MSSqlIndexManager.CreateUniqueIndexSql</c> override, measured rather than read: the
    /// collapsed producer must still create a genuinely UNIQUE index through the index manager on MSSql.
    /// </summary>
    [Fact]
    public async Task The_index_manager_can_create_a_unique_index_on_mssql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{TableName}]");
        NewConnector().CreateTable(new[] { typeof(MsIdxRow) });

        var manager = new Birko.Data.SQL.MSSql.IndexManagement.MSSqlIndexManager(NewConnector());
        var definition = new Birko.Data.Patterns.IndexManagement.IndexDefinition
        {
            Name = "ux_msidxrows_manager",
            Unique = true,
            Fields = new[]
            {
                new Birko.Data.Patterns.IndexManagement.IndexField { Name = "TenantGuid" },
                new Birko.Data.Patterns.IndexManagement.IndexField { Name = "Status" }
            }
        };

        await manager.CreateAsync(definition, TableName, CancellationToken.None);

        IndexColumns("ux_msidxrows_manager").Should().Equal(new[] { "TenantGuid", "Status" });
        IsUnique("ux_msidxrows_manager").Should().BeTrue(
            "ToSqlIndexDefinition used to drop Unique, which is exactly why a parallel unique emitter existed");
    }

    [Fact]
    public async Task A_declared_unique_index_is_enforced_on_mssql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{TableName}]");
        NewConnector().CreateTable(new[] { typeof(MsIdxRow) });

        var store = new AsyncMSSqlStore<MsIdxRow>();
        store.SetSettings(Settings());

        var tenantA = Guid.NewGuid();
        await store.CreateAsync(new MsIdxRow { Guid = Guid.NewGuid(), TenantGuid = tenantA, Number = "FV1", Status = "open" });
        await store.Invoking(s => s.CreateAsync(new MsIdxRow { Guid = Guid.NewGuid(), TenantGuid = tenantA, Number = "FV1", Status = "open" }))
                   .Should().ThrowAsync<Exception>();
    }
}
