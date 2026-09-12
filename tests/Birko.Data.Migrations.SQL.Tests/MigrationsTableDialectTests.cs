using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Migrations;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.SQL;
using Birko.Data.Migrations.SQL.Settings;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// TASK-332 — the migrations state table must be creatable, writable and readable on <b>every</b> supported
/// dialect, not just the two it happened to work on.
///
/// <para>
/// <b>What was wrong.</b> <c>SqlMigrationStore</c> built one hardcoded <c>CREATE TABLE</c> for all
/// providers. Two independent defects followed, both of which fire on Birko's own bookkeeping table
/// <i>before</i> a single model table is reached, so the SQL migration runner worked on SQLite and
/// PostgreSQL only:
/// </para>
/// <list type="number">
///   <item><b>Identifiers were ANSI double-quoted on every dialect.</b> The <c>quoteOpen</c>/<c>quoteClose</c>
///   constructor parameters defaulted to <c>"</c> and <b>nothing anywhere passed them</b> — measured, 0 call
///   sites across the framework, its tests and all 16 consumer repos — even though
///   <c>SqlMigrationRunner</c> constructs the store while holding the connector, and therefore already knows
///   the dialect. MySQL accepts <c>"</c> as an identifier delimiter only under <c>ANSI_QUOTES</c>; measured
///   on 8.4.11 the statement is <c>ERROR 1064 … near '"__Migrations_X" ("Version" BIGINT PRIMAR'</c>.</item>
///   <item><b>The DDL declared <c>TIMESTAMP</c> twice.</b> In T-SQL that is a deprecated synonym for
///   <c>ROWVERSION</c> — a binary row-version type of which a table may have at most one. Measured on SQL
///   Server 2022 CU26 (16.0.4275.2): <i>Msg 2738, A table can only have one timestamp column.</i></item>
/// </list>
///
/// <para>
/// ⚠ <b>The two defects do not overlap, and a measurement explains why the consumer saw two different
/// errors.</b> <c>Microsoft.Data.SqlClient</c> connects with <c>QUOTED_IDENTIFIER ON</c>, under which SQL
/// Server <i>does</i> accept <c>"</c> as an identifier delimiter — verified directly, the same statement is
/// <c>Msg 102 Incorrect syntax</c> under <c>sqlcmd</c>'s default <c>OFF</c> and reaches <c>Msg 2738</c> with
/// <c>-I</c>. So defect 1 is MySQL-only in practice and defect 2 is MSSql-only. Fixing either alone leaves
/// one server broken, which is why both are pinned here and per dialect.
/// </para>
///
/// <para>
/// <b>Why the assertion is a round-trip and not "the DDL did not throw".</b> The bug was found because
/// something <i>read</i> the table afterwards. <c>GetAppliedVersions</c> is also the wrong oracle on its own:
/// it answers <b>empty</b> for an absent table rather than failing (see <c>TableExists</c>), so an empty set
/// is indistinguishable from a table that was never created. Recording a version and reading that exact
/// version back is what proves the table exists, accepts a write and can be queried.
/// </para>
///
/// <para>
/// Each dialect's live half is gated on its own <c>BIRKO_*_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to turn a
/// missing server into a failure rather than a skip. The SQLite subclass needs no server and always runs.
/// </para>
/// </summary>
public abstract class MigrationsTableDialectTestsBase : IDisposable
{
    private readonly ITestOutputHelper _output;

    protected MigrationsTableDialectTestsBase(ITestOutputHelper output) => _output = output;

    /// <summary>Short, dialect-unique infix so parallel subclasses cannot collide on a table name.</summary>
    protected abstract string Moniker { get; }

    /// <summary>Human name for skip messages.</summary>
    protected abstract string DialectName { get; }

    /// <summary>The env var that turns this dialect on, or null when it needs no server.</summary>
    protected abstract string? HostVariable { get; }

    /// <summary>The connector under test. Only called once <see cref="Available"/> is true.</summary>
    protected abstract AbstractConnector Connector();

    /// <summary>A raw connection for setup/teardown, independent of the store.</summary>
    protected abstract DbConnection OpenRaw();

    /// <summary>True when this dialect can actually be exercised here.</summary>
    protected virtual bool Available => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable!));

    /// <summary>Any per-dialect bootstrap (SQL Server needs its database to exist).</summary>
    protected virtual void Bootstrap() { }

    private static bool RequireLive
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    protected bool RequireServer()
    {
        if (Available)
        {
            Bootstrap();
            return true;
        }

        var message = $"SKIPPED: no live {DialectName}. Set {HostVariable} to exercise this test; "
                    + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive)
        {
            throw new InvalidOperationException(message);
        }
        return false;
    }

    protected string MigrationsTable(string path) => $"__MigDlg{Moniker}{path}";
    protected string ModelTable(string path) => $"MigDlg{Moniker}{path}";

    /// <summary>
    /// Drops a table if present. Written per dialect rather than shared because the DDL to drop a table is
    /// the one thing that cannot be routed through the connector under test without making cleanup depend on
    /// the code being tested.
    /// </summary>
    protected abstract void DropIfExists(DbConnection connection, string table);

    protected void Clean(params string[] tables)
    {
        if (!Available) return;
        try
        {
            using var connection = OpenRaw();
            foreach (var table in tables)
            {
                try { DropIfExists(connection, table); } catch { /* best effort */ }
            }
        }
        catch
        {
            // Teardown must never fail a run.
        }
    }

    public virtual void Dispose()
        => Clean(MigrationsTable("Sync"), ModelTable("Sync"), MigrationsTable("Async"), ModelTable("Async"));

    private sealed class CreateThingMigration : AbstractMigration
    {
        private readonly string _table;
        public CreateThingMigration(string table) => _table = table;

        public override long Version => 20260912001;
        public override string Name => "CreateThing";
        public override string Description => "TASK-332 dialect closure";

        public override void Up(IMigrationContext context)
            => context.Schema.CreateCollection(_table)
                .WithField("Id", FieldType.Guid, isPrimary: true)
                .WithField("Label", FieldType.String, maxLength: 100)
                .Build();
    }

    // ---------------------------------------------------------------------------------------------
    // Offline: the emitted statement, pinned per dialect. Always runs, needs no server -- so a mutation
    // is provable on a machine with no databases at all, and MySQL's ERROR 1064 has a cheap local guard.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Every identifier in the migrations DDL is delimited the way <b>this connector</b> delimits one.
    /// </summary>
    /// <remarks>
    /// The connector is the oracle rather than a literal, deliberately: TASK-269 records that the declared
    /// side of a column has exactly one producer and that a test which spells an expected type out by hand
    /// becomes a second one. So this asserts the <i>relationship</i> (quoting comes from the connector) and
    /// the two <i>defects</i> below concretely, and never restates the type table.
    /// </remarks>
    [Fact]
    public void The_migrations_ddl_quotes_every_identifier_with_this_dialects_delimiters()
    {
        if (!Available) { SkipOfflineIfUnavailable(); return; }

        var connector = Connector();
        var sql = Ddl(connector, "__MigDdlProbe");

        sql.Should().Contain(connector.QualifiedIdentifier("__MigDdlProbe"),
            "the table reference must come from the connector, not from an ANSI default hardcoded in the store");

        foreach (var column in new[] { "Version", "Name", "Description", "CreatedAt", "AppliedAt" })
        {
            sql.Should().Contain(connector.QuoteIdentifier(column),
                $"column {column} must be delimited by this provider's own quoting");
        }
    }

    /// <summary>
    /// Whether this dialect has a length-enforcing string type at all. False for SQLite alone, whose
    /// <c>ConvertType</c> answers <c>TEXT</c> for every string — correctly, since it has no such type.
    /// </summary>
    protected virtual bool DeclaresStringLength => true;

    /// <summary>
    /// The declared length of <c>Name</c> survives into the statement — and on the one dialect that has no
    /// length-enforcing string type, its absence is asserted as <b>correct</b> rather than tolerated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pins TASK-264's trap without naming a type: a connector reads a column's width off the field's
    /// <i>runtime type</i>, so building the column from a bare <c>AbstractField</c> rather than through
    /// <c>SchemaField.For</c> silently yields the unbounded type and loses the 255.
    /// </para>
    /// <para>
    /// Both sides are asserted because TASK-264 records SQLite's <c>TEXT</c> as the right answer there, and a
    /// one-sided test invites the next reader to "fix" SQLite into a divergence from its own convention.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_migrations_ddl_keeps_the_declared_length_of_name()
    {
        if (!Available) { SkipOfflineIfUnavailable(); return; }

        var sql = Ddl(Connector(), "__MigDdlProbe");

        if (DeclaresStringLength)
        {
            sql.Should().Contain("255",
                "Name is declared as a 255-char column and a connector only emits a length for a CharField");
        }
        else
        {
            sql.Should().NotContain("255",
                "SQLite has no length-enforcing string type, so TEXT is the correct answer here (TASK-264) "
              + "-- this asserts that, so nobody later 'fixes' it into a divergence");
        }
    }

    private void SkipOfflineIfUnavailable()
    {
        // The offline pins need a connector, and a connector needs settings that name a host. They are
        // therefore gated like the live ones -- but SQLite, which needs no server, always runs them.
        RequireServer();
    }

    internal static string Ddl(AbstractConnector connector, string table)
    {
        var store = new SqlMigrationStore(
            () => throw new InvalidOperationException("the DDL is composed without opening a connection"),
            connector,
            new SqlMigrationSettings { MigrationsTable = table });
        return store.CreateMigrationsTableSql();
    }

    // ---------------------------------------------------------------------------------------------
    // Live: create, record, read back -- on the sync and the async path separately, because the two
    // carried two copies of the statement and a fix to one would leave the other broken.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_migration_creates_the_state_table_and_its_version_reads_back()
    {
        if (!RequireServer()) return;

        var migrations = MigrationsTable("Sync");
        var model = ModelTable("Sync");
        Clean(migrations, model);

        var connector = Connector();
        var runner = new SqlMigrationRunner(connector, new SqlMigrationSettings
        {
            MigrationsTable = migrations,
            UseTransaction = false,
        });
        runner.RegisterMigrations(new CreateThingMigration(model));

        runner.Initialize();
        var result = runner.Migrate();

        result.Success.Should().BeTrue(
            $"the migrations state table must be creatable on {DialectName} -- before TASK-332 this failed "
          + "while creating __Migrations, before any model table was reached");

        // The read-back is the assertion that matters: GetAppliedVersions answers EMPTY for an absent table,
        // so only a version that comes back proves the table was created, written and queried.
        runner.Store.GetAppliedVersions().Should().Contain(20260912001,
            "the recorded version must be readable from the state table afterwards");
        runner.Store.GetCurrentVersion().Should().Be(20260912001);
    }

    [Fact]
    public async Task The_async_path_creates_the_state_table_and_round_trips_a_version()
    {
        if (!RequireServer()) return;

        var migrations = MigrationsTable("Async");
        var model = ModelTable("Async");
        Clean(migrations, model);

        var connector = Connector();
        var settings = new SqlMigrationSettings { MigrationsTable = migrations, UseTransaction = false };
        var store = new SqlMigrationStore(() => connector.CreateConnection(connector.Settings), connector, settings);

        // InitializeAsync reaches CreateMigrationsTableAsync -- the second copy of the statement, which a
        // fix applied only to the sync path would have left emitting the broken DDL.
        await store.InitializeAsync();

        var migration = new CreateThingMigration(model);
        await store.RecordMigrationAsync(migration);

        (await store.GetAppliedVersionsAsync()).Should().Contain(20260912001,
            $"the async INSERT and SELECT must both be valid on {DialectName}");
        (await store.GetCurrentVersionAsync()).Should().Be(20260912001);

        await store.RemoveMigrationAsync(migration);
        (await store.GetAppliedVersionsAsync()).Should().BeEmpty(
            "the async DELETE targets the same table and columns");
    }
}
