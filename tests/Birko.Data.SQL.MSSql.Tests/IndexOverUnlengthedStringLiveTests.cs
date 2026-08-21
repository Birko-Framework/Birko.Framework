using System;
using System.Collections.Generic;
using System.Linq;
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
/// TASK-257, the index half — a declared index or constraint over an <b>unlengthed</b> string column is
/// actually built on a real SQL Server.
///
/// <para>
/// <b>Why this is a separate suite from <see cref="StringPredicateLiveTests"/>.</b> The two halves of the fix
/// are independent, and this one is not fixed by the other: measured on 2022 (16.0.4265.3), an index over
/// <c>NVARCHAR(MAX)</c> raises the <i>same</i> <b>Msg 1919</b> as one over <c>TEXT</c>
/// ("Column 'x' … is of a type that is invalid for use as a key column in an index"). So mapping unlengthed
/// strings to <c>NVARCHAR(MAX)</c> alone would have fixed every predicate and left every declared index
/// exactly as broken. Hence the bounded branch, gated on <c>AbstractField.IsInIndexKey</c>.
/// </para>
/// <para>
/// <b>Why "it did not throw" is worth nothing here.</b> Since TASK-204 schema-ensure attempts one index per
/// statement and <i>records</i> a failure on <c>IndexCreationFailures</c> rather than faulting — and nothing
/// in the tree subscribes. So an unbuildable index has always been silent. These tests assert against
/// <c>sys.indexes</c> and against that collection being empty.
/// </para>
/// <para>
/// <b>UNIQUE and PRIMARY KEY are the worse case</b>, and they are the ones <c>IsIndexed</c> cannot see:
/// <c>FieldDefinition</c> emits them as inline column constraints, so before this fix they took down the
/// entire <c>CREATE TABLE</c> rather than just an index.
/// </para>
/// <para>
/// Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make a skip a failure.
/// </para>
/// </summary>
public class IndexOverUnlengthedStringLiveTests : IDisposable
{
    private const string PerPropertyTable = "MsIdxPerProp";
    private const string CompositeTable = "MsIdxComposite";
    private const string ConstraintTable = "MsIdxConstraint";
    private const string PrimaryTable = "MsIdxPrimary";

    private const string PerPropertyIndex = "ix_msidxperprop_code";
    private const string CompositeUniqueIndex = "ux_msidxcomposite_number";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public IndexOverUnlengthedStringLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port)
    {
        TrustServerCertificate = true
    };

    // ---- probe entities. No [MaxLengthField] on any indexed column — that is the point.

    /// <summary>Per-property <c>[IndexedField]</c> — <c>DataBase_Table.cs</c>'s first marking site.</summary>
    [Table(PerPropertyTable)]
    public class PerPropRow : AbstractDatabaseLogModel
    {
        [IndexedField(PerPropertyIndex)]
        public string Code { get; set; } = null!;

        public string Payload { get; set; } = null!;
    }

    /// <summary>
    /// Class-level <c>[CompositeIndex]</c> with <c>IsUnique</c> over an unlengthed string — consumer Symbio's
    /// exact shape (a docnumber UNIQUE composite over (TenantGuid, Number)), and the second marking site.
    /// </summary>
    [Table(CompositeTable)]
    [CompositeIndex(CompositeUniqueIndex, nameof(TenantGuid), nameof(Number), IsUnique = true)]
    public class CompositeRow : AbstractDatabaseLogModel
    {
        public Guid TenantGuid { get; set; }
        public string Number { get; set; } = null!;
    }

    /// <summary>
    /// Inline constraints. Before the fix this entity's <c>CREATE TABLE</c> failed outright — the table did
    /// not exist at all, so nothing about it worked.
    /// </summary>
    [Table(ConstraintTable)]
    public class ConstraintRow : AbstractDatabaseLogModel
    {
        [UniqueField]
        public string Sku { get; set; } = null!;
    }

    /// <summary>
    /// A string <c>[PrimaryField]</c>, which is the <c>IsPrimary</c> disjunct of <c>IsInIndexKey</c> and the
    /// one with no other live coverage.
    /// <para>
    /// Derives from <c>AbstractLogModel</c>, <b>not</b> <c>AbstractDatabaseLogModel</c>, deliberately: the
    /// latter marks its inherited <c>Guid</c> <c>[PrimaryField]</c>, so a second inline <c>PRIMARY KEY</c>
    /// would fail with Msg 8110 ("more than one key specified") and the test would prove nothing about
    /// column typing. This base leaves <c>Guid</c> a plain column, so the string is the only key.
    /// </para>
    /// <para>
    /// DDL-only — no row is written. <c>AbstractLogModel</c> leaves <c>CreatedAt</c>/<c>UpdatedAt</c> at
    /// <c>default(DateTime)</c> = 0001-01-01, which is below SQL Server's 1753 floor, so an INSERT here would
    /// fail for a reason that has nothing to do with what is being tested.
    /// </para>
    /// </summary>
    [Table(PrimaryTable)]
    public class PrimaryRow : AbstractLogModel
    {
        [PrimaryField]
        public string NaturalKey { get; set; } = null!;

        public string Payload { get; set; } = null!;
    }

    private static MSSqlConnector NewConnector() => new(Settings());

    private static void Exec(string sql)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static List<string> IndexColumns(string table, string index)
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
        cmd.Parameters.AddWithValue("@t", table);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static bool IsUniqueIndex(string table, string index)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_unique FROM sys.indexes WHERE name = @i AND object_id = OBJECT_ID(@t)";
        cmd.Parameters.AddWithValue("@i", index);
        cmd.Parameters.AddWithValue("@t", table);
        return cmd.ExecuteScalar() is bool b && b;
    }

    private static List<string> PrimaryKeyColumns(string table)
    {
        var result = new List<string>();
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT c.name
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
WHERE i.is_primary_key = 1 AND i.object_id = OBJECT_ID(@t)
ORDER BY ic.key_ordinal";
        cmd.Parameters.AddWithValue("@t", table);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static bool TableExists(string table)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = @t";
        cmd.Parameters.AddWithValue("@t", table);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static string ColumnType(string table, string column)
    {
        using var conn = new SqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT t.name, c.max_length
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(@t) AND c.name = @c";
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@c", column);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return "(missing)";
        return $"{reader.GetString(0)}({reader.GetInt16(1)})";
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        foreach (var t in new[] { PerPropertyTable, CompositeTable, ConstraintTable, PrimaryTable })
        {
            try { Exec($"DROP TABLE IF EXISTS [{t}]"); } catch { }
        }
    }

    // ---------------------------------------------------------------- the two marking sites

    [Fact]
    public void A_per_property_index_over_an_unlengthed_string_is_built()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{PerPropertyTable}]");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(PerPropRow) });

        IndexColumns(PerPropertyTable, PerPropertyIndex).Should().Equal(new[] { "Code" },
            "before the fix the column was TEXT and this index could not be created — Msg 1919, recorded "
          + "on IndexCreationFailures and silent");
        connector.IndexCreationFailures.Should().BeEmpty(
            "schema-ensure records rather than throws, so an empty failure list is the real assertion");

        ColumnType(PerPropertyTable, nameof(PerPropRow.Code)).Should().Be("nvarchar(510)",
            "255 characters = 510 bytes; a MAX type cannot be an index key at all");
        ColumnType(PerPropertyTable, nameof(PerPropRow.Payload)).Should().Be("nvarchar(-1)",
            "the un-indexed sibling keeps the unbounded type — the bound is per column, not per entity");
    }

    [Fact]
    public void A_composite_unique_index_over_an_unlengthed_string_is_built()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{CompositeTable}]");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(CompositeRow) });

        IndexColumns(CompositeTable, CompositeUniqueIndex).Should().Equal(new[] { "TenantGuid", "Number" },
            "the class-level attribute is LoadIndexes' other marking site, and this is Symbio's real shape");
        IsUniqueIndex(CompositeTable, CompositeUniqueIndex).Should().BeTrue(
            "a missing UNIQUE index is a missing constraint — it silently accepts the duplicates the "
          + "declaration exists to forbid (TASK-246)");
        connector.IndexCreationFailures.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- the inline-constraint case

    [Fact]
    public void A_unique_constraint_over_an_unlengthed_string_creates_the_table_at_all()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{ConstraintTable}]");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(ConstraintRow) });

        // This is the case IsIndexed could not see: LoadIndexes never marks a [UniqueField] column, and
        // FieldDefinition emits UNIQUE inline — so before the fix the whole CREATE TABLE raised Msg 1919.
        TableExists(ConstraintTable).Should().BeTrue(
            "an inline UNIQUE over TEXT (and over NVARCHAR(MAX)) fails the CREATE TABLE itself, not just "
          + "an index — measured on 2022");
        ColumnType(ConstraintTable, nameof(ConstraintRow.Sku)).Should().Be("nvarchar(510)");
    }

    [Fact]
    public void A_primary_key_over_an_unlengthed_string_creates_the_table_at_all()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{PrimaryTable}]");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(PrimaryRow) });

        // The IsPrimary disjunct, live. Same mechanism as the UNIQUE case: FieldDefinition emits PRIMARY KEY
        // inline, LoadIndexes never marks the column, and NVARCHAR(MAX) is refused as a key with Msg 1919 --
        // so before this fix the CREATE TABLE itself failed and the table simply did not exist.
        TableExists(PrimaryTable).Should().BeTrue(
            "an inline PRIMARY KEY over NVARCHAR(MAX) raises Msg 1919, killing the CREATE TABLE");
        ColumnType(PrimaryTable, nameof(PrimaryRow.NaturalKey)).Should().Be("nvarchar(510)");
        ColumnType(PrimaryTable, nameof(PrimaryRow.Payload)).Should().Be("nvarchar(-1)",
            "only the key column is narrowed");

        PrimaryKeyColumns(PrimaryTable).Should().Equal(new[] { nameof(PrimaryRow.NaturalKey) },
            "and it is genuinely the primary key, not merely a column of the right width");
    }

    [Fact]
    public async Task The_unique_constraint_actually_constrains()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{ConstraintTable}]");

        var store = new AsyncMSSqlStore<ConstraintRow>();
        store.SetSettings(Settings());

        await store.CreateAsync(new ConstraintRow { Guid = Guid.NewGuid(), Sku = "SKU-1" });

        // A bounded column was chosen over a prefix index precisely so the constraint is not weaker than
        // declared. Assert it bites rather than assuming the index's existence implies it.
        var duplicate = async () => await store.CreateAsync(new ConstraintRow { Guid = Guid.NewGuid(), Sku = "SKU-1" });

        // Assert the SQL error NUMBER, not merely that something threw. This file's own doc argues that
        // "it did not throw" is worth nothing here; "it threw" is the same weakness inverted -- a connection
        // failure or an Invalid-object-name would satisfy ThrowAsync<Exception> and keep the test green while
        // the constraint had quietly stopped existing. 2627 = unique CONSTRAINT violation, 2601 = unique
        // INDEX violation; SQL Server picks between them by how the uniqueness was declared, so accept either.
        (await SqlErrorNumbersOf(duplicate)).Should().Contain(n => n == 2627 || n == 2601,
            "the UNIQUE constraint must reject a duplicate, and for that reason specifically");
    }

    // ---------------------------------------------------------------- the stated cost of the bound

    [Fact]
    public async Task An_over_long_value_in_an_indexed_column_is_refused_loudly()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{PerPropertyTable}]");

        var store = new AsyncMSSqlStore<PerPropRow>();
        store.SetSettings(Settings());

        // The deliberate, documented cost of NVARCHAR(255): a 300-character value that a TEXT column would
        // have accepted is now refused. Loud-and-narrow beats quiet-and-weak (TASK-248) — a prefix index
        // would instead have silently rejected two genuinely different values sharing a prefix. Not available
        // on SQL Server anyway.
        var tooLong = async () => await store.CreateAsync(new PerPropRow
        {
            Guid = Guid.NewGuid(),
            Code = new string('x', 300),
            Payload = "fine",
        });

        // 2628 is the modern "String or binary data would be truncated in table ..., column ..." (it names the
        // column); 8152 is the legacy message. Either proves the write was REFUSED rather than silently
        // truncated -- which is the whole justification for bounding the column, and which depends on
        // ANSI_WARNINGS being ON (SqlClient's default; the framework never changes it). With it OFF the value
        // is truncated to 255 instead and a later distinct value collides on the constraint.
        (await SqlErrorNumbersOf(tooLong)).Should().Contain(n => n == 2628 || n == 8152,
            "the refusal must be a truncation refusal, not any old exception -- a truncation instead of a "
          + "refusal is what would silently weaken every bounded unique column");
    }

    [Fact]
    public async Task An_over_long_value_in_an_UNindexed_column_is_still_accepted()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS [{PerPropertyTable}]");

        var store = new AsyncMSSqlStore<PerPropRow>();
        store.SetSettings(Settings());

        // The other side of the same decision, and the reason the unindexed default is MAX rather than a
        // bounded number: no write that worked before this change starts failing.
        var payload = new string('y', 5000);
        await store.CreateAsync(new PerPropRow { Guid = Guid.NewGuid(), Code = "short", Payload = payload });

        var rows = (await store.ReadAsync(x => x.Code == "short", null, null, null, CancellationToken.None)).ToList();
        rows.Should().HaveCount(1);
        rows[0].Payload.Should().Be(payload, "an unbounded column must round-trip a long value intact");
    }

    /// <summary>
    /// Every <c>SqlException.Number</c> in the thrown exception's chain (including
    /// <c>SqlException.Errors</c>), so a test can assert <i>why</i> a write failed rather than merely that it
    /// did. Returns an empty set when nothing threw, which fails the <c>Contain</c> assertions above with a
    /// readable message.
    /// </summary>
    private static async Task<List<int>> SqlErrorNumbersOf(Func<Task> action)
    {
        var numbers = new List<int>();
        try
        {
            await action();
            return numbers;
        }
        catch (Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is not SqlException sql) continue;
                numbers.Add(sql.Number);
                foreach (SqlError err in sql.Errors) numbers.Add(err.Number);
            }

            // A non-SqlException (or one wrapped so the chain is lost) must not read as a pass.
            if (numbers.Count == 0) numbers.Add(-1);
            return numbers;
        }
    }

}
