using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-245 — every declared index on MySQL was absent, because the framework emitted syntax MySQL
/// rejects.
///
/// <para>
/// <see cref="AbstractConnectorBase.CreateIndexSql"/> emits
/// <c>CREATE {UNIQUE }INDEX IF NOT EXISTS …</c>. <b>MySQL does not support <c>IF NOT EXISTS</c> on
/// <c>CREATE INDEX</c></b> — measured on MySQL 8.4 as ERROR 1064, a syntax error, so the statement never
/// ran. MSSql overrides the emitter with a <c>sys.indexes</c> guard and PostgreSQL/SQLite support the
/// clause, which left MySQL as the one supported provider that neither overrode nor supported it. So every
/// <c>[IndexedField]</c> and <c>[CompositeIndex]</c> on a MySQL entity produced no index — and for a
/// UNIQUE one, no constraint.
/// </para>
///
/// <para>
/// <b>Why it shipped unnoticed.</b> TASK-204 deliberately made schema-ensure <i>record</i> an unbuildable
/// index rather than throw, because an unbuildable index used to take down the entity's whole read
/// surface. That is the right call and this was its cost: the failure landed in
/// <see cref="AbstractConnector.IndexCreationFailures"/> and raised <c>OnIndexCreationFailed</c>, and a
/// host subscribing to neither saw nothing at all. The degradation is not silent by accident — it is
/// silent because nobody is listening. No live MySQL suite existed before TASK-242, and the index tests
/// that do exist run on SQLite, which accepts the clause.
/// </para>
///
/// <para>
/// <b>The fix</b> is a MySQL <c>CreateIndexSql</c> override that emits no conditional clause, plus
/// tolerance of error <b>1061</b> (<c>Duplicate key name</c>, i.e. "it is already there") at the
/// <c>CreateIndexes</c> funnel. 1061 is what the other three providers already report as success. Error
/// <b>1062</b> (<c>Duplicate entry</c> — a UNIQUE index over data that already violates it) is a
/// genuinely unbuildable index and is still <b>recorded, not thrown</b>, so TASK-204 survives intact.
/// Measured: the two codes are distinct, and 1062 stays 1062 on a repeat attempt.
/// </para>
///
/// <para>
/// <b>Every assertion here queries <c>information_schema.statistics</c> or counts committed rows.</b>
/// "Nothing threw" passes against the broken code — schema-ensure swallows — and an empty
/// <c>IndexCreationFailures</c> is only the weaker companion claim. The catalogue is the evidence.
/// </para>
///
/// <para>
/// <b>Indexed string columns are deliberately bounded.</b> A plain <c>string</c> maps to
/// <c>LONGTEXT</c> on MySQL, and MySQL cannot index a BLOB/TEXT column without a key length — measured
/// ERROR 1170. That is a separate defect from this one (column type, not statement syntax) and is tracked
/// as its own task; <see cref="An_index_over_an_unbounded_string_is_still_unbuildable_on_mysql"/> pins the
/// boundary so this suite cannot be mistaken for proof that it was fixed here. Using
/// <c>[MaxLengthField]</c> in the criterion tests is what isolates the 1064 defect rather than shaping the
/// model until something passes.
/// </para>
/// </summary>
public class DeclaredIndexLiveTests : IDisposable
{
    private const string TableName = "IdxRows";
    private const string TextTableName = "IdxTextRows";
    private const string UniqueIndex = "ux_idxrows_docnum";
    private const string PlainIndex = "ix_idxrows_status";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public DeclaredIndexLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host))
        {
            return true;
        }
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return false;
    }

    private static MySqlSettings Settings() => new(Host!, Database, User, Password, Port);

    // ---------------------------------------------------------------- models

    /// <summary>
    /// The table as it exists in a deployment that predates the index declaration — identical columns, no
    /// index attributes. Lets a test seed rows that make the UNIQUE index unbuildable before schema-ensure
    /// ever attempts it, which is the TASK-204 shape.
    /// </summary>
    [Table(TableName)]
    public class LegacyIdxRow : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }

        [MaxLengthField(64)]
        public string Number { get; set; } = null!;

        [MaxLengthField(32)]
        public string Status { get; set; } = null!;
    }

    /// <summary>
    /// The same table with two class-level indexes — one UNIQUE, one not, so a single schema-ensure covers
    /// both halves of acceptance criterion 1 and also proves the per-index loop does not stop at the first
    /// index. <c>CREATE TABLE IF NOT EXISTS</c> is a no-op over
    /// <see cref="LegacyIdxRow"/>'s table, so only the indexes are new.
    /// </summary>
    [Table(TableName)]
    [CompositeIndex(UniqueIndex, nameof(TenantGuid), nameof(Number), IsUnique = true)]
    [CompositeIndex(PlainIndex, nameof(Status), nameof(Number))]
    public class IdxRow : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }

        [MaxLengthField(64)]
        public string Number { get; set; } = null!;

        [MaxLengthField(32)]
        public string Status { get; set; } = null!;
    }

    /// <summary>An indexed <c>string</c> with no length — <c>LONGTEXT</c> on MySQL. See the class remarks.</summary>
    [Table(TextTableName)]
    [CompositeIndex("ix_idxtext_note", nameof(Note))]
    public class IdxTextRow : AbstractLogModel
    {
        public string Note { get; set; } = null!;
    }

    // ---------------------------------------------------------------- plumbing

    private static void Exec(string sql)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS `{TableName}`"); } catch { }
        try { Exec($"DROP TABLE IF EXISTS `{TextTableName}`"); } catch { }
    }

    /// <summary>
    /// The columns of one index, in key order, read from the catalogue.
    /// <para>
    /// <c>TABLE_SCHEMA = DATABASE()</c> is not optional: without it this probe has the same cross-schema
    /// false positive as <c>SqlIndexManager.IndexExistsSql</c> and would report an index that the fix never
    /// created. <c>NON_UNIQUE = 0</c> means unique; <c>SEQ_IN_INDEX</c> is 1-based.
    /// </para>
    /// </summary>
    private static List<(string Column, bool Unique)> IndexColumns(string table, string index)
    {
        var result = new List<(string, bool)>();
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COLUMN_NAME, NON_UNIQUE FROM information_schema.statistics "
                        + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND INDEX_NAME = @i "
                        + "ORDER BY SEQ_IN_INDEX";
        cmd.Parameters.AddWithValue("@t", table);
        cmd.Parameters.AddWithValue("@i", index);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add((reader.GetString(0), reader.GetInt32(1) == 0));
        }
        return result;
    }

    private static int IndexCount(string table)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.statistics "
                        + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND INDEX_NAME <> 'PRIMARY'";
        cmd.Parameters.AddWithValue("@t", table);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int CommittedCount(string table)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM `{table}`";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static bool TableExists(string table)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables "
                        + "WHERE table_schema = DATABASE() AND table_name = @t";
        cmd.Parameters.AddWithValue("@t", table);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>A connector of its own, so <c>IndexCreationFailures</c> is observable per test.</summary>
    private static MySQLConnector NewConnector() => new(Settings());

    private static AsyncMySQLStore<IdxRow> AsyncStore()
    {
        var store = new AsyncMySQLStore<IdxRow>();
        store.SetSettings(Settings());
        return store;
    }

    private static IdxRow Row(Guid tenant, string number, string status = "open")
        => new() { Guid = Guid.NewGuid(), TenantGuid = tenant, Number = number, Status = status };

    /// <summary>Finds the recorded 1061/1062-style driver error inside the framework's exception wrapping.</summary>
    private static MySqlException? DriverErrorIn(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is MySqlException my) return my;
        }
        return null;
    }

    // ---------------------------------------------------------------- criterion 1

    [Fact]
    public void Declared_indexes_are_present_in_information_schema_after_schema_ensure()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(IdxRow) });

        IndexColumns(TableName, UniqueIndex).Should().Equal(
            new List<(string, bool)> { ("TenantGuid", true), ("Number", true) },
            "the declared UNIQUE composite must exist on MySQL, in key order — against the unfixed code "
          + "CREATE INDEX IF NOT EXISTS was ERROR 1064 and no index was created at all");

        IndexColumns(TableName, PlainIndex).Should().Equal(
            new List<(string, bool)> { ("Status", false), ("Number", false) },
            "and the non-unique composite alongside it, proving the per-index loop did not stop at the first");

        // The weaker companion assertion, stated second and deliberately not on its own: an empty failure
        // list is not proof an index exists (an untouched entity has attempted nothing).
        connector.IndexCreationFailures.Should().BeEmpty();
    }

    [Fact]
    public async Task Declared_indexes_are_present_after_async_schema_ensure()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");

        var connector = NewConnector();
        await connector.CreateTableAsync(new[] { typeof(IdxRow) }, CancellationToken.None);

        // NOTE: the collection overload, not Equal(params string[]) — that one would read the "because"
        // string as a third expected element.
        IndexColumns(TableName, UniqueIndex).Select(x => x.Column).Should().Equal(
            new[] { "TenantGuid", "Number" },
            "the async schema-ensure loop is separate code from the sync one — a one-sided fix would ship a "
          + "MySQL where sync stores index and async ones do not");
        IndexColumns(TableName, PlainIndex).Should().HaveCount(2);
        connector.IndexCreationFailures.Should().BeEmpty();
    }

    /// <summary>
    /// For a UNIQUE index the <b>constraint</b> is the point, not the catalogue row — so this asserts the
    /// engine actually rejects the duplicate, and that uniqueness is per-tenant rather than global.
    /// </summary>
    [Fact]
    public async Task A_declared_unique_index_is_enforced_by_the_engine()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        NewConnector().CreateTable(new[] { typeof(IdxRow) });

        var store = AsyncStore();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await store.CreateAsync(Row(tenantA, "FV2026000001"));

        await store.Invoking(s => s.CreateAsync(Row(tenantA, "FV2026000001")))
                   .Should().ThrowAsync<Exception>("the same (tenant, number) pair must violate the constraint");

        await store.CreateAsync(Row(tenantB, "FV2026000001"));

        CommittedCount(TableName).Should().Be(2, "uniqueness is per-tenant, not global");
    }

    // ---------------------------------------------------------------- criterion 2

    /// <summary>
    /// The second schema-ensure over an already-indexed table must record nothing and duplicate nothing.
    /// <para>
    /// <b>Round-trip cost, stated explicitly because the criterion asks:</b> option 2 costs <b>zero</b>
    /// extra round trips. Exactly one <c>CREATE INDEX</c> is attempted per declared index per
    /// schema-ensure, which is what the unfixed code already did — it simply always failed with 1064.
    /// A probe-then-emit design (option 1) would have cost one additional round trip per index per
    /// schema-ensure, i.e. per request under scoped stores.
    /// </para>
    /// </summary>
    [Fact]
    public void A_second_schema_ensure_over_an_indexed_table_records_nothing_and_duplicates_nothing()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");

        var connector = NewConnector();
        var raised = new List<IndexCreationFailure>();
        connector.OnIndexCreationFailed += raised.Add;

        connector.CreateTable(new[] { typeof(IdxRow) });
        connector.CreateTable(new[] { typeof(IdxRow) });   // the "already there" run — 1061 on MySQL

        raised.Should().BeEmpty("an index that is already present is not a failure — 1061 means "
                              + "'already there', which the other three providers report as success");
        connector.IndexCreationFailures.Should().BeEmpty();

        IndexCount(TableName).Should().Be(2, "no duplicate index may be created");
        IndexColumns(TableName, UniqueIndex).Should().HaveCount(2);
        IndexColumns(TableName, PlainIndex).Should().HaveCount(2);
    }

    /// <summary>
    /// The scoped-store shape: a fresh connector over the same database re-runs schema-ensure. This is the
    /// per-request case that made TASK-204's failure list grow by one entry per request.
    /// </summary>
    [Fact]
    public void A_schema_ensure_on_a_fresh_connector_over_an_indexed_table_records_nothing()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        NewConnector().CreateTable(new[] { typeof(IdxRow) });

        var second = NewConnector();
        second.CreateTable(new[] { typeof(IdxRow) });

        second.IndexCreationFailures.Should().BeEmpty();
        IndexCount(TableName).Should().Be(2);
    }

    // ---------------------------------------------------------------- criterion 3 (TASK-204 must survive)

    [Fact]
    public async Task A_unique_index_over_violating_data_is_recorded_not_thrown()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");

        // Create the table WITHOUT the indexes, then seed the duplicate pair that makes the UNIQUE one
        // unbuildable. CREATE TABLE IF NOT EXISTS is then a no-op and only the indexes are new.
        NewConnector().CreateTable(new[] { typeof(LegacyIdxRow) });
        var tenant = Guid.NewGuid();
        var legacy = new AsyncMySQLStore<LegacyIdxRow>();
        legacy.SetSettings(Settings());
        await legacy.CreateAsync(new LegacyIdxRow { Guid = Guid.NewGuid(), TenantGuid = tenant, Number = "DUP", Status = "open" });
        await legacy.CreateAsync(new LegacyIdxRow { Guid = Guid.NewGuid(), TenantGuid = tenant, Number = "DUP", Status = "open" });

        var connector = NewConnector();
        connector.Invoking(c => c.CreateTable(new[] { typeof(IdxRow) }))
                 .Should().NotThrow("an unbuildable index must not take the entity's whole surface down (TASK-204)");

        connector.IndexCreationFailures.Should().HaveCount(1);
        var failure = connector.IndexCreationFailures[0];
        failure.TableName.Should().Be(TableName);
        failure.IndexName.Should().Be(UniqueIndex);

        // Without this the test passes against the UNFIXED tree, which also records a failure — for 1064.
        // 1062 is the code that means "genuinely unbuildable"; 1061 is the one that is tolerated.
        var driverError = DriverErrorIn(failure.Error);
        driverError.Should().NotBeNull("the recorded error must carry the driver's own exception");
        ((int)driverError!.ErrorCode).Should().Be(1062,
            "1062 Duplicate entry is the unbuildable case; 1064 would mean the syntax defect is still present "
          + "and 1061 would mean the tolerance swallowed the wrong thing");

        IndexColumns(TableName, UniqueIndex).Should().BeEmpty("the unbuildable index is absent");
        IndexColumns(TableName, PlainIndex).Should().HaveCount(2,
            "one index per statement — a failure must not hide the buildable indexes behind it");

        var store = AsyncStore();
        (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(2,
            "the read surface stays reachable — that is what TASK-204 exists to protect, and the rows needed "
          + "to repair the duplicate must be readable through the very store whose index failed");
    }

    [Fact]
    public async Task A_unique_index_over_violating_data_is_recorded_not_thrown_async()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");

        NewConnector().CreateTable(new[] { typeof(LegacyIdxRow) });
        var tenant = Guid.NewGuid();
        var legacy = new AsyncMySQLStore<LegacyIdxRow>();
        legacy.SetSettings(Settings());
        await legacy.CreateAsync(new LegacyIdxRow { Guid = Guid.NewGuid(), TenantGuid = tenant, Number = "DUP", Status = "open" });
        await legacy.CreateAsync(new LegacyIdxRow { Guid = Guid.NewGuid(), TenantGuid = tenant, Number = "DUP", Status = "open" });

        var connector = NewConnector();
        await connector.Invoking(c => c.CreateTableAsync(new[] { typeof(IdxRow) }, CancellationToken.None))
                       .Should().NotThrowAsync();

        connector.IndexCreationFailures.Should().HaveCount(1);
        ((int)DriverErrorIn(connector.IndexCreationFailures[0].Error)!.ErrorCode).Should().Be(1062);
        IndexColumns(TableName, PlainIndex).Should().HaveCount(2);
    }

    // ---------------------------------------------------------------- criterion 4 (TASK-243 must survive)

    /// <summary>
    /// Index DDL is DDL, so it must keep going through <c>DoDdlCommand</c>: on MySQL that suppresses the
    /// ambient boundary, because <b>MySQL implicitly commits an open transaction on any DDL statement</b>.
    /// A cold store's first write inside a boundary therefore issues both CREATE TABLE and CREATE INDEX off
    /// the boundary, and the caller's rollback must still undo the caller's rows.
    /// </summary>
    [Fact]
    public async Task Index_ddl_from_a_cold_store_inside_a_boundary_does_not_commit_it()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");

        var store = AsyncStore();   // deliberately NOT warmed up: schema-ensure happens inside the boundary

        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new List<IdxRow>
            {
                Row(Guid.NewGuid(), "R1"), Row(Guid.NewGuid(), "R2"), Row(Guid.NewGuid(), "R3")
            }, null, CancellationToken.None);
            await uow.RollbackAsync();
        }

        CommittedCount(TableName).Should().Be(0, "the caller's rows must not survive their own rollback");

        // MySQL's pinned answer, opposite to PostgreSQL/MSSql/SQLite: schema DDL issued during a boundary
        // is NOT rolled back with it, because it never ran on that boundary's connection. Asserted so that
        // nobody "unifies" the providers from symmetry — see CLAUDE.md § Conventions (TASK-243).
        TableExists(TableName).Should().BeTrue();
        IndexCount(TableName).Should().Be(2, "and the declared indexes survive with it");
    }

    [Fact]
    public async Task A_committed_boundary_over_a_cold_store_keeps_its_rows_and_its_indexes()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");

        var store = AsyncStore();
        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            await store.CreateAsync(new List<IdxRow>
            {
                Row(Guid.NewGuid(), "C1"), Row(Guid.NewGuid(), "C2"), Row(Guid.NewGuid(), "C3")
            }, null, CancellationToken.None);
            await uow.CommitAsync();
        }

        CommittedCount(TableName).Should().Be(3, "the committed control — without it the rollback assertion "
                                              + "would pass on a store that simply never wrote anything");
        IndexCount(TableName).Should().Be(2);
    }

    // ---------------------------------------------------------------- public contract pins

    /// <summary>
    /// The public <c>CreateIndexes</c> becomes idempotent on MySQL. Deliberate: it already is on the other
    /// three (SQLite/PostgreSQL via <c>IF NOT EXISTS</c>, MSSql via its <c>sys.indexes</c> guard), and its
    /// one external caller — the migrations <c>SqlIndexBuilder.Build</c> — wants a re-applied migration to
    /// succeed rather than fail on MySQL alone.
    /// </summary>
    [Fact]
    public void Explicit_CreateIndexes_is_idempotent_for_an_already_present_index()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(IdxRow) });

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(IdxRow));
        var index = table!.Indexes![PlainIndex];

        connector.Invoking(c => c.CreateIndexes(TableName, new[] { index }))
                 .Should().NotThrow("1061 means the index the caller asked for is already there");

        IndexCount(TableName).Should().Be(2);
    }

    /// <summary>
    /// …and the half that did NOT change: an index that genuinely cannot be built still throws from the
    /// public path. That is TASK-204's documented contract, and it is about <i>unbuildable</i> (1062), not
    /// about "already present" (1061).
    /// </summary>
    [Fact]
    public async Task Explicit_CreateIndexes_still_throws_for_an_unbuildable_unique_index()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        NewConnector().CreateTable(new[] { typeof(LegacyIdxRow) });

        var tenant = Guid.NewGuid();
        var legacy = new AsyncMySQLStore<LegacyIdxRow>();
        legacy.SetSettings(Settings());
        await legacy.CreateAsync(new LegacyIdxRow { Guid = Guid.NewGuid(), TenantGuid = tenant, Number = "DUP", Status = "open" });
        await legacy.CreateAsync(new LegacyIdxRow { Guid = Guid.NewGuid(), TenantGuid = tenant, Number = "DUP", Status = "open" });

        var connector = NewConnector();
        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(IdxRow));
        var unique = table!.Indexes![UniqueIndex];

        var thrown = connector.Invoking(c => c.CreateIndexes(TableName, new[] { unique }))
                              .Should().Throw<Exception>("an explicit caller asking for this index now must fail loudly")
                              .Which;

        // The code, not merely "something threw". Against the unfixed tree this test passed for the WRONG
        // reason — it threw 1064 (the syntax defect) rather than 1062 (genuinely unbuildable), so without
        // this assertion it could not tell a fix from a no-op.
        ((int)DriverErrorIn(thrown)!.ErrorCode).Should().Be(1062,
            "the statement must now reach the server and fail on the DATA, not on its own syntax");

        connector.IndexCreationFailures.Should().BeEmpty("nothing is recorded outside schema-ensure");
    }

    /// <summary>
    /// The explicit opt-out. <c>throwIfExists: true</c> means the same thing on every provider — the
    /// conditional form is dropped and an already-present index raises. Without parameterising the emitter
    /// the flag would have been honourable on MySQL alone and silently ignored on the three providers whose
    /// conditional DDL cannot raise, which is the silent-drop shape § Conventions ranks worst.
    /// </summary>
    [Fact]
    public void Explicit_CreateIndexes_with_throwIfExists_raises_for_an_already_present_index()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(IdxRow) });

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(IdxRow));
        var index = table!.Indexes![PlainIndex];

        var thrown = connector.Invoking(c => c.CreateIndexes(TableName, new[] { index }, throwIfExists: true))
                              .Should().Throw<Exception>().Which;

        ((int)DriverErrorIn(thrown)!.ErrorCode).Should().Be(1061,
            "the tolerance is what the flag turns off — the server still reports Duplicate key name");

        connector.Invoking(c => c.CreateIndexes(TableName, new[] { index }))
                 .Should().NotThrow("and the default remains an ensure");

        IndexCount(TableName).Should().Be(2, "neither call may duplicate the index");
    }

    // ---------------------------------------------------------------- DROP INDEX (same clause, adjacent method)

    /// <summary>
    /// <c>DropIndexSql</c> carried the identical defect: the base emits <c>DROP INDEX IF EXISTS `n`</c>,
    /// which on MySQL is ERROR 1064 for the <c>IF EXISTS</c> <b>and</b> omits the mandatory
    /// <c>ON &lt;table&gt;</c> — wrong twice over.
    /// </summary>
    [Fact]
    public void DropIndexes_removes_a_declared_index_on_mysql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(IdxRow) });

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(IdxRow));
        connector.DropIndexes(TableName, new[] { table!.Indexes![PlainIndex] });

        IndexColumns(TableName, PlainIndex).Should().BeEmpty("the index must actually be gone");
        IndexColumns(TableName, UniqueIndex).Should().HaveCount(2, "and only that one");
    }

    /// <summary>
    /// Dropping an absent index throws (MySQL 1091). Deliberately NOT tolerated: the base's
    /// <c>IF EXISTS</c> did tolerate it, but a <c>DropIndexes</c> caller asked for a specific index, and
    /// the migrations <c>DropIndex</c> step should fail loudly rather than silently skip.
    /// </summary>
    [Fact]
    public void DropIndexes_for_an_absent_index_throws()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(IdxRow) });

        var absent = new Birko.Data.SQL.Tables.IndexDefinition { Name = "ix_not_there" };
        var thrown = connector.Invoking(c => c.DropIndexes(TableName, new[] { absent }))
                              .Should().Throw<Exception>().Which;

        // 1091 "Can't DROP", not 1064: the statement must be syntactically valid and fail because the index
        // genuinely is not there. Against the unfixed tree this threw 1064 and the test passed anyway.
        ((int)DriverErrorIn(thrown)!.ErrorCode).Should().Be(1091);
    }

    // ---------------------------------------------------------------- IIndexManager uniformity (TASK-249)

    /// <summary>
    /// <c>IIndexManager.CreateAsync</c> must behave the same on MySQL as everywhere else for an index that is
    /// already present.
    /// </summary>
    /// <remarks>
    /// This path executes through <c>SqlIndexManager</c>'s own connection and deliberately bypasses
    /// <c>AbstractConnector.CreateIndexes</c>, so it did <b>not</b> inherit that funnel's 1061 tolerance. The
    /// result was a manager that was non-uniform in a way TASK-245 actually made worse: before, MySQL failed
    /// for every index (1064); after, it succeeded for a new index and threw <c>IndexManagementException</c>
    /// for an existing one, while SQLite/PostgreSQL (native <c>IF NOT EXISTS</c>) and MSSql (a synthesised
    /// guard) reported success. Found by this task's own close-gate review.
    /// </remarks>
    [Fact]
    public async Task The_index_manager_tolerates_an_already_present_index_on_mysql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        NewConnector().CreateTable(new[] { typeof(IdxRow) });

        var manager = new Birko.Data.SQL.MySQL.IndexManagement.MySqlIndexManager(NewConnector());
        var definition = new Birko.Data.Patterns.IndexManagement.IndexDefinition
        {
            Name = "ix_idxrows_mgr",
            Fields = new[] { new Birko.Data.Patterns.IndexManagement.IndexField { Name = "Status" } }
        };

        await manager.CreateAsync(definition, TableName, CancellationToken.None);
        IndexColumns(TableName, "ix_idxrows_mgr").Should().HaveCount(1);

        await manager.Invoking(m => m.CreateAsync(definition, TableName, CancellationToken.None))
                     .Should().NotThrowAsync("1061 means the index asked for is already there, which every "
                                           + "other provider reports as success");

        IndexColumns(TableName, "ix_idxrows_mgr").Should().HaveCount(1, "and nothing is duplicated");
    }

    /// <summary>
    /// The other half of the same verb family — dropping an already-absent index. Fixing create and leaving
    /// drop would ship a manager whose create tolerates "already there" beside a drop that throws for
    /// "already gone", on one provider only.
    /// </summary>
    [Fact]
    public async Task The_index_manager_tolerates_an_already_absent_index_on_mysql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        NewConnector().CreateTable(new[] { typeof(IdxRow) });

        var manager = new Birko.Data.SQL.MySQL.IndexManagement.MySqlIndexManager(NewConnector());

        await manager.Invoking(m => m.DropAsync("ix_never_existed", TableName, CancellationToken.None))
                     .Should().NotThrowAsync("MySQL accepts no IF EXISTS on DROP INDEX, so 1091 is how it "
                                           + "reports what the other providers no-op");

        // …but a real index still drops, so the tolerance is not swallowing the work.
        await manager.DropAsync(PlainIndex, TableName, CancellationToken.None);
        IndexColumns(TableName, PlainIndex).Should().BeEmpty();
    }

    /// <summary>
    /// The connector's own <c>DropIndexes</c> must NOT gain that tolerance: a caller naming a specific index
    /// should fail loudly, and the migrations drop step depends on it. Asserted so the two doors cannot be
    /// "unified" from symmetry.
    /// </summary>
    [Fact]
    public void The_connector_drop_still_throws_for_an_absent_index()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(IdxRow) });

        var absent = new Birko.Data.SQL.Tables.IndexDefinition { Name = "ix_never_existed" };
        var thrown = connector.Invoking(c => c.DropIndexes(TableName, new[] { absent }))
                              .Should().Throw<Exception>().Which;

        ((int)DriverErrorIn(thrown)!.ErrorCode).Should().Be(1091);
    }

    // ---------------------------------------------------------------- the boundary of this fix

    /// <summary>
    /// <b>Not fixed here, and pinned so that is unmistakable.</b> A plain <c>string</c> maps to
    /// <c>LONGTEXT</c> on MySQL (<c>MySQLConnector.ConvertType</c>), and MySQL cannot index a BLOB/TEXT
    /// column without a key length — measured ERROR <b>1170</b>, for UNIQUE and non-unique alike. So an
    /// index declared over an unbounded string is still unbuildable on MySQL after this task: a different
    /// cause (column type, not statement syntax) needing a different fix (map indexed strings to a bounded
    /// type, or emit a key length), tracked as its own task.
    /// <para>
    /// It is recorded rather than thrown, exactly like any other unbuildable index, so this is a
    /// degradation and not an outage. The assertion is on the <b>error code</b> — if a future change makes
    /// this build, this test fails and says so, which is what stops the boundary silently moving.
    /// </para>
    /// </summary>
    [Fact]
    public void An_index_over_an_unbounded_string_is_still_unbuildable_on_mysql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TextTableName}`");

        var connector = NewConnector();
        connector.Invoking(c => c.CreateTable(new[] { typeof(IdxTextRow) }))
                 .Should().NotThrow("recorded, not thrown — the table and its read surface stay usable");

        connector.IndexCreationFailures.Should().HaveCount(1);
        var driverError = DriverErrorIn(connector.IndexCreationFailures[0].Error);
        ((int)driverError!.ErrorCode).Should().Be(1170,
            "BLOB/TEXT column used in key specification without a key length — a separate defect from the "
          + "IF NOT EXISTS syntax this task fixes. If this ever becomes buildable, update the task that owns it");

        TableExists(TextTableName).Should().BeTrue("the table itself is created regardless");
    }
}
