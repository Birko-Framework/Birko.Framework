using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-296 — <b>the journal mode this framework puts a SQLite database into, and the opt-out.</b>
///
/// <para>These are the deterministic guards. The load reproduction that motivated the change lives in
/// <c>ColdTableStormTests</c> and is a race by nature; what is protected here is the part a regression
/// would actually break — the default, and a consumer's ability to refuse it.</para>
///
/// <para><b>Why WAL is the default at all.</b> On SQLite's own rollback journal, a statement on a
/// <b>pooled</b> <c>sqlite3</c> handle can be answered from a schema image older than a
/// <c>CREATE TABLE</c> another connection has already committed — so a freshly created table reads as
/// missing, and since TASK-285 a count of a missing table answers <c>0</c>, silently. Measured: the same
/// storm fires on <b>7 of 7</b> runs on the rollback journal and <b>0 of 5</b> on WAL. It is also faster
/// on every axis measured, which is not the reason but is worth knowing.</para>
/// </summary>
public class JournalModeTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;
    private static int _seq;

    public JournalModeTests(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(), $"birko-journal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    /// <remarks>No process-wide a process-wide pool clear — see [[TASK-276]].</remarks>
    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("JournalRows")]
    public class JournalRow : AbstractDatabaseModel
    {
        public string? Value { get; set; }
    }

    private SqLiteSettings Fresh() => new(_root, $"journal{Interlocked.Increment(ref _seq)}.db");

    private static SqLiteConnector Connector(SqLiteSettings settings)
        => (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(settings);

    /// <summary>Asks the file itself, on a connection of its own — never the connector's own answer.</summary>
    private static string JournalModeInFile(SqLiteSettings settings)
    {
        using var db = new SqliteConnection(settings.GetConnectionString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        return Convert.ToString(cmd.ExecuteScalar()) ?? "(null)";
    }

    private static async Task<AsyncSQLiteStore<JournalRow>> Touch(SqLiteSettings settings)
    {
        var store = new AsyncSQLiteStore<JournalRow>();
        store.SetSettings(settings);
        await store.CreateAsync(new JournalRow { Guid = Guid.NewGuid(), Value = "x" });
        return store;
    }

    // ── the default ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_shipped_default_puts_the_database_into_WAL()
    {
        var settings = Fresh();
        settings.JournalMode.Should().Be("WAL", "the default is the fix; changing it reopens TASK-290");

        var store = await Touch(settings);

        JournalModeInFile(settings).Should().Be("wal");
        Connector(settings).JournalModeInEffect.Should().Be("wal");
        Connector(settings).JournalModeFailure.Should().BeNull();
        (await store.CountAsync()).Should().Be(1, "and the store still works, which is the point");
    }

    /// <summary>
    /// WAL is <b>persistent in the file</b>, which is why applying it once per connector is enough — and
    /// is also the consequence a consumer has to know about, since it outlives the process.
    /// </summary>
    [Fact]
    public async Task The_mode_persists_in_the_file_beyond_the_process_that_set_it()
    {
        var settings = Fresh();
        await Touch(settings);

        // A connection that knows nothing about this framework still finds WAL.
        using var raw = new SqliteConnection($"Data Source={settings.Path}");
        raw.Open();
        using var cmd = raw.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        Convert.ToString(cmd.ExecuteScalar()).Should().Be("wal");

        File.Exists(settings.Path + "-wal").Should().BeTrue(
            "the sidecar is the operational consequence: a backup that copies only the .db file can lose "
            + "recent commits. It is documented on SqLiteSettings.JournalMode for that reason");
    }

    // ── the opt-outs, which § SH-H037 requires to be tested rather than described ───────────────────

    /// <summary>
    /// The opt-out back to SQLite's own rollback journal. Meaningful because <c>DELETE</c> takes a
    /// database <b>out</b> of WAL persistently — so this starts the file in WAL and shows it reverted,
    /// rather than asserting "delete" on a fresh database, where delete is the default anyway and the
    /// assertion would pass however the code behaved.
    /// </summary>
    [Fact]
    public async Task An_explicit_DELETE_is_honoured_so_the_old_behaviour_is_reachable()
    {
        var settings = Fresh();

        // Start in WAL, the only mode that persists, so "reverted" is distinguishable from "never set".
        settings.JournalMode = "WAL";
        await Touch(settings);
        JournalModeInFile(settings).Should().Be("wal", "the premise");

        // A second connector for the same file would be the cached one, so use fresh settings pointing at
        // the same path — DataBase.GetConnector keys on Location:Name, so this is deliberately the SAME
        // connector, and the once-per-connector guard has already fired. Apply DELETE on a connector of
        // its own instead, which is what a differently-configured process would do.
        var reverting = new SqLiteSettings(_root, settings.Name!) { JournalMode = "DELETE" };
        var connector = new SqLiteConnector(reverting);
        connector.CreateConnection(reverting).Dispose();

        connector.JournalModeInEffect.Should().Be("delete");
        connector.JournalModeFailure.Should().BeNull();
        JournalModeInFile(settings).Should().Be("delete",
            "DELETE is the documented way back out of WAL, and it persists");
    }

    /// <summary>
    /// The full opt-out: null or blank emits <b>no PRAGMA at all</b> and leaves whatever the file has.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The distinctive mode has to be WAL, and two earlier versions of this test were flaky for
    /// getting that wrong.</b> They put the file into <c>TRUNCATE</c> and expected to find it there
    /// later — but SQLite persists a journal mode in the file <b>only for WAL</b>; the rollback modes are
    /// per-connection, so a new connection reports <c>delete</c> and the assertion failed at random
    /// depending on whether pooling happened to hand back the same handle. Measured in
    /// <see cref="Which_journal_modes_persist_across_connections"/>, which is in this file precisely
    /// because that is what it cost.
    /// <para>
    /// So: put the file into WAL by hand, opt out, and show the framework neither changed it nor even
    /// tried — <c>JournalModeInEffect</c> staying null is the discriminator that no file state could give.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_null_or_blank_mode_emits_no_PRAGMA_and_leaves_the_file_alone(string? mode)
    {
        var settings = Fresh();
        settings.JournalMode = mode;

        using (var setup = new SqliteConnection(settings.GetConnectionString()))
        {
            setup.Open();
            using var make = setup.CreateCommand();
            make.CommandText = "CREATE TABLE Seed (Id INTEGER); INSERT INTO Seed VALUES (1);";
            make.ExecuteNonQuery();
            using var cmd = setup.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL";
            Convert.ToString(cmd.ExecuteScalar()).Should().Be("wal");
        }

        var store = await Touch(settings);

        Connector(settings).JournalModeInEffect.Should().BeNull(
            "the opt-out emits no PRAGMA at all — this is the discriminator, because a file state alone "
            + "could not distinguish 'left alone' from 'set to the default'");
        Connector(settings).JournalModeFailure.Should().BeNull();
        JournalModeInFile(settings).Should().Be("wal", "and the file kept the mode it already had");
        (await store.CountAsync()).Should().Be(1);
    }

    // ── the whitelist: the value is interpolated into a PRAGMA that takes no parameter ──────────────

    /// <summary>
    /// <c>PRAGMA journal_mode=…</c> accepts no parameter and quotes nothing, so the whitelist <b>is</b>
    /// the containment — § Conventions' identifier family at a fourth kind of sink, a bare keyword in
    /// statement position, where refusal is the only mechanism left (TASK-255's reasoning).
    /// </summary>
    [Theory]
    [InlineData("WAL; DROP TABLE JournalRows; --")]
    [InlineData("wal--")]
    [InlineData("NONSENSE")]
    // The four SQLite accepts but that do NOT persist between connections, so this seam cannot deliver
    // them. Accepting one would take the value and silently do nothing.
    [InlineData("TRUNCATE")]
    [InlineData("PERSIST")]
    [InlineData("MEMORY")]
    [InlineData("OFF")]
    public async Task An_unsupported_mode_is_refused_and_RECORDED_and_the_store_still_works(string mode)
    {
        var settings = Fresh();
        settings.JournalMode = mode;

        var store = await Touch(settings);
        var connector = Connector(settings);

        _out.WriteLine($"failure: {connector.JournalModeFailure?.Message}");

        connector.JournalModeInEffect.Should().BeNull("nothing was applied");
        connector.JournalModeFailure.Should().BeOfType<ArgumentException>()
            .Which.Message.Should().Contain("Set SqLiteSettings.JournalMode to null or empty",
                "a guard whose message only says 'no' gets reached around — § SH-H037");
        connector.JournalModeFailure!.Message.Should().Contain("only for WAL",
            "and it says WHY the rollback modes are refused, or the refusal reads as arbitrary");
        JournalModeInFile(settings).Should().Be("delete", "the payload never reached the database");

        (await store.CountAsync()).Should().Be(1,
            "an unusable journal mode must not take the store down with it: this is recorded, not thrown, "
            + "on the same terms as IndexCreationFailures (TASK-204)");
    }

    /// <summary>Case is not the caller's problem, so the whitelist matches insensitively.</summary>
    [Theory]
    [InlineData("wal")]
    [InlineData("Wal")]
    [InlineData(" WAL ")]
    public async Task The_mode_is_matched_case_and_whitespace_insensitively(string mode)
    {
        var settings = Fresh();
        settings.JournalMode = mode;

        await Touch(settings);

        Connector(settings).JournalModeInEffect.Should().Be("wal");
        Connector(settings).JournalModeFailure.Should().BeNull();
    }

    // ── shape ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applied <b>once</b> per connector, not per statement — proved by making the setting invalid
    /// <i>after</i> the first application and showing nothing notices.
    /// </summary>
    /// <remarks>
    /// The connector holds the settings instance it was created with, so mutating that instance is
    /// exactly what a per-statement implementation would pick up. An earlier version of this test simply
    /// asserted the mode was still right after twenty operations, which a per-statement implementation
    /// would also have satisfied — it pinned the outcome and not the once-ness.
    /// <para>
    /// Once is sufficient because <c>journal_mode</c> is persistent in the file, and it matters because
    /// the PRAGMA runs on a connection of its own: per statement it would be an extra connection open on
    /// every single read.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_mode_is_applied_once_rather_than_per_statement()
    {
        var settings = Fresh();
        var store = await Touch(settings);
        var connector = Connector(settings);

        connector.JournalModeInEffect.Should().Be("wal");
        connector.JournalModeFailure.Should().BeNull();

        // Poison the setting the connector is holding. A per-statement implementation would read this on
        // the very next operation and record a failure.
        settings.JournalMode = "NOT A MODE";

        for (var i = 0; i < 20; i++)
        {
            await store.CreateAsync(new JournalRow { Guid = Guid.NewGuid(), Value = $"v{i}" });
            await store.CountAsync();
        }

        connector.JournalModeFailure.Should().BeNull(
            "the guard runs once per connector; if this fails, the PRAGMA is being re-applied per "
            + "statement, which costs an extra connection open on every read");
        connector.JournalModeInEffect.Should().Be("wal");
        JournalModeInFile(settings).Should().Be("wal");
        (await store.CountAsync()).Should().Be(21);
    }

    [Fact]
    public void LoadFrom_carries_the_journal_mode()
    {
        var source = new SqLiteSettings("a", "b") { JournalMode = "TRUNCATE", CommandTimeout = 7 };
        var target = new SqLiteSettings();

        target.LoadFrom(source);

        target.JournalMode.Should().Be("TRUNCATE",
            "a setting that LoadFrom drops is a setting a consumer's configuration cannot reach");
        target.CommandTimeout.Should().Be(7);
    }

    /// <summary>
    /// Step 0 for the whitelist: <b>which journal modes actually persist between connections?</b>
    /// </summary>
    [Fact]
    public void Which_journal_modes_persist_across_connections()
    {
        foreach (var mode in new[] { "TRUNCATE", "PERSIST", "MEMORY", "OFF", "WAL", "DELETE" })
        {
            var settings = Fresh();
            settings.JournalMode = null;                    // keep the framework out of it
            using (var seed = new SqliteConnection(settings.GetConnectionString() + ";Pooling=False"))
            {
                seed.Open();
                using var make = seed.CreateCommand();
                make.CommandText = "CREATE TABLE X (Id INTEGER); INSERT INTO X VALUES (1);";
                make.ExecuteNonQuery();
                using var set = seed.CreateCommand();
                set.CommandText = "PRAGMA journal_mode=" + mode;
                _out.WriteLine($"  {mode,-9} set -> {Convert.ToString(set.ExecuteScalar())}");
            }
            using (var check = new SqliteConnection(settings.GetConnectionString() + ";Pooling=False"))
            {
                check.Open();
                using var cmd = check.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode";
                _out.WriteLine($"  {mode,-9} new connection sees -> {Convert.ToString(cmd.ExecuteScalar())}");
            }
        }
    }
}
