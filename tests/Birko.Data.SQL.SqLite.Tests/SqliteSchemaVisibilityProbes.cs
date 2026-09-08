using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-290 — <b>what SQLite itself does</b>, measured below the framework, so the mechanism behind the
/// schema-ensure escape is separated from the framework behaviour layered on top of it.
///
/// <para>These are probes, not contract tests: they assert the driver's and the engine's rules so that a
/// later reader can tell which of TASK-290's hypotheses were killed by measurement rather than by
/// argument. Each one names the hypothesis it settles.</para>
///
/// <para>⚠ <b>Rollback-journal mode is not incidental here.</b> The framework never emits a
/// <c>journal_mode</c> — <c>SqLiteSettings.GetConnectionString()</c> writes only <c>Data Source</c>, an
/// optional <c>Password</c> and <c>Default Timeout</c> — so every Birko SQLite database runs on SQLite's
/// default <c>delete</c> journal, which is what the consumer measured in production. The visibility rules
/// probed here are specific to that mode.</para>
/// </summary>
public class SqliteSchemaVisibilityProbes : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public SqliteSchemaVisibilityProbes(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(), $"birko-vis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    /// <remarks>
    /// ⚠ <b>No a process-wide pool clear here, deliberately — measured.</b> Twenty-four of
    /// this project's teardowns call it, and it is <b>process-wide</b>: it reaches every other test class's
    /// pooled connections, including ones with a command in flight, because xUnit runs collections in
    /// parallel. Adding three more copies of it took this suite from 6/6 clean to a reproducible ~1-2 in 6
    /// cross-class failure (<c>TransactionBoundaryEndToEndTests</c> and this class's own
    /// <c>BeginTransaction</c>, both with SQLITE_BUSY). Removing it from the classes added here took it
    /// back to clean. Each test owns its own database file, so there is nothing for a pool clear to buy.
    /// <para>
    /// That is a data point for [[TASK-276]], whose leading hypothesis for the pre-existing rare
    /// cross-class failure in <c>Birko.Data.SQL.Tests</c> was exactly these calls, and which killed it by
    /// measuring one clear against one in-flight connection in isolation. It reproduces at suite scale
    /// rather than in isolation — do not read that task's "wrong, and now recorded as wrong" as covering
    /// this.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Db(string name) => Path.Combine(_root, name);

    /// <summary>The connection string the framework actually builds, so these probes measure its shape.</summary>
    private string Cs(string name, int timeoutSeconds = 30)
        => $"Data Source={Db(name)};Default Timeout={timeoutSeconds}";

    private static long SchemaVersion(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA schema_version";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar()) ?? "(null)";
    }

    // ── the baseline: what an UNCOMMITTED create looks like to another connection ────────────────────

    /// <summary>
    /// The rule the whole stale-image hypothesis rests on: a table created in another connection's
    /// <b>open</b> transaction is reported <c>no such table</c> — <b>not</b> <c>SQLITE_BUSY</c>.
    /// </summary>
    [Fact]
    public void AnUncommittedCreate_IsNoSuchTableToAnotherConnection_AndNotBusy()
    {
        using var writer = new SqliteConnection(Cs("uncommitted.db"));
        writer.Open();
        using var tx = writer.BeginTransaction();
        using (var cmd = writer.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS T (Id INTEGER)";
            cmd.ExecuteNonQuery();
        }

        using var reader = new SqliteConnection(Cs("uncommitted.db", timeoutSeconds: 2));
        reader.Open();
        var act = () => Scalar(reader, "SELECT count(*) FROM T");

        var thrown = act.Should().Throw<SqliteException>().Which;
        _out.WriteLine($"uncommitted-create read: code={thrown.SqliteErrorCode} msg={thrown.Message}");

        thrown.SqliteErrorCode.Should().Be(1,
            "SQLITE_ERROR, not SQLITE_BUSY(5) — this is what makes 'no such table with no lock error' a "
            + "legitimate answer rather than a contradiction, and it is the only measured shape that "
            + "reproduces the consumer's 'Error 5 = 0' alongside a missing-table report");
        thrown.Message.Should().Contain("no such table");

        tx.Rollback();
    }

    /// <summary>
    /// And the other half: once committed, it is visible immediately to a connection that was already
    /// open and had already read the schema. Kills the "pooled handle keeps a stale schema cache"
    /// hypothesis for the simple case.
    /// </summary>
    [Fact]
    public void ACommittedCreate_IsImmediatelyVisibleToAnAlreadyOpenConnection()
    {
        using var reader = new SqliteConnection(Cs("committed.db"));
        reader.Open();
        // Force the reader to have read and cached a schema BEFORE the create.
        Scalar(reader, "SELECT count(*) FROM sqlite_master");
        var before = SchemaVersion(reader);

        using (var writer = new SqliteConnection(Cs("committed.db")))
        {
            writer.Open();
            using var tx = writer.BeginTransaction();
            using (var cmd = writer.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "CREATE TABLE T (Id INTEGER)";
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        var after = SchemaVersion(reader);
        _out.WriteLine($"schema_version before={before} after={after}");

        Scalar(reader, "SELECT count(*) FROM T").Should().Be("0",
            "a connection re-validates the schema cookie at the start of every statement, so there is no "
            + "stale-cache window for a COMMITTED create on an idle connection");
        after.Should().BeGreaterThan(before, "the cookie is the signal a reader uses to reload");
    }

    /// <summary>
    /// The interesting case: the reader is holding a read transaction OPEN across the writer's commit.
    /// This is the only shape in which a committed table is legitimately invisible, and it is what
    /// "answered against a schema image older than those CREATE TABLEs" means concretely.
    /// </summary>
    [Fact]
    public async Task AReaderInsideAnOpenReadTransaction_DoesNotSeeACreateCommittedAfterItStarted()
    {
        using var reader = new SqliteConnection(Cs("snapshot.db"));
        reader.Open();
        using (var seed = reader.CreateCommand())
        {
            seed.CommandText = "CREATE TABLE Anchor (Id INTEGER)";
            seed.ExecuteNonQuery();
        }

        using var readTx = reader.BeginTransaction();
        Scalar(reader, "SELECT count(*) FROM Anchor");   // takes SHARED and holds it
        var insideVersion = SchemaVersion(reader);

        Exception? writerFailure = null;
        var writerDone = Task.Run(() =>
        {
            try
            {
                using var writer = new SqliteConnection(Cs("snapshot.db", timeoutSeconds: 5));
                writer.Open();
                using var tx = writer.BeginTransaction();
                using var cmd = writer.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "CREATE TABLE Later (Id INTEGER)";
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            catch (Exception ex) { writerFailure = ex; }
        });

        await writerDone.WaitAsync(TimeSpan.FromSeconds(15));

        string readerAnswer;
        try { readerAnswer = Scalar(reader, "SELECT count(*) FROM Later"); }
        catch (SqliteException ex) { readerAnswer = $"ERROR {ex.SqliteErrorCode}: {ex.Message}"; }

        _out.WriteLine($"writer failure: {writerFailure?.GetType().Name}: "
            + $"{(writerFailure as SqliteException)?.SqliteErrorCode}");
        _out.WriteLine($"reader inside its read transaction sees: {readerAnswer} "
            + $"(schema_version inside={insideVersion}, now={SchemaVersion(reader)})");

        readTx.Rollback();
    }

    // ── the hot-journal hypothesis: can a COMMITTED table be undone? ─────────────────────────────────

    /// <summary>
    /// <b>Hypothesis: a rollback journal that could not be deleted is treated as HOT by the next
    /// connection, which rolls it back — undoing a transaction the writer was told had committed.</b>
    ///
    /// <para>It is worth measuring because it is the only candidate that explains <i>several</i>
    /// recently-created tables disappearing together: <c>sqlite_master</c> rows share pages, so restoring
    /// a page pre-image removes every table whose row lives on it. In <c>journal_mode=delete</c> the
    /// commit is finalised by <b>deleting</b> the journal file, and on Windows a delete fails while
    /// another handle is open on it — an antivirus or indexer scanning a busy database directory is the
    /// textbook case.</para>
    ///
    /// <para>The probe holds the journal open itself, which is the deterministic stand-in for that.</para>
    /// </summary>
    [Fact]
    public void HoldingTheRollbackJournalOpen_AcrossACommit()
    {
        var path = Db("hot.db");
        using (var seedConnection = new SqliteConnection(Cs("hot.db")))
        {
            seedConnection.Open();
            using var seed = seedConnection.CreateCommand();
            seed.CommandText = "PRAGMA journal_mode=delete; CREATE TABLE Anchor (Id INTEGER);";
            seed.ExecuteNonQuery();
        }
        // TASK-276 -- precise: this probe owns "hot.db", and Cs() is the exact connection string.
        SqlitePool.ClearFor(Cs("hot.db"));

        FileStream? journalHandle = null;
        Exception? commitFailure = null;
        try
        {
            using (var writer = new SqliteConnection(Cs("hot.db")))
            {
                writer.Open();
                using var tx = writer.BeginTransaction();
                using (var cmd = writer.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "CREATE TABLE Later (Id INTEGER)";
                    cmd.ExecuteNonQuery();
                }

                // The journal exists now, mid-transaction. Grab a handle on it so SQLite's delete at
                // commit cannot succeed. FileShare.Delete is deliberately NOT granted.
                var journal = path + "-journal";
                File.Exists(journal).Should().BeTrue("a rollback-journal write transaction must have one");
                try
                {
                    journalHandle = new FileStream(journal, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite);
                }
                catch (Exception ex)
                {
                    _out.WriteLine($"could not open the journal: {ex.GetType().Name}: {ex.Message}");
                }

                try { tx.Commit(); }
                catch (Exception ex) { commitFailure = ex; }
            }

            _out.WriteLine($"commit failure: {commitFailure?.GetType().Name}: {commitFailure?.Message}");
            _out.WriteLine($"journal still present: {File.Exists(path + "-journal")}");

            journalHandle?.Dispose();
            journalHandle = null;
            SqlitePool.ClearFor(Cs("hot.db"));   // TASK-276 -- precise

            using var after = new SqliteConnection(Cs("hot.db"));
            after.Open();
            var tables = Scalar(after, "SELECT group_concat(name) FROM sqlite_master WHERE type='table'");
            _out.WriteLine($"tables after reopening: {tables}");
        }
        finally
        {
            journalHandle?.Dispose();
        }
    }

    // ─────────── TASK-290: the mechanism, below the framework, with the cookie read ───────────

    private static bool RequireStorm(ITestOutputHelper output)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_STORM")))
        {
            return true;
        }
        output.WriteLine("SKIPPED: set BIRKO_STORM to run this load probe (~15 s).");
        return false;
    }

    /// <summary>
    /// <b>TASK-290's answer, reproduced with raw Microsoft.Data.Sqlite and nothing of this framework in
    /// the way — so the failing connection is ours and its schema cookie can be read.</b>
    ///
    /// <para>The shape is the framework's, faithfully: a writer that opens a connection per
    /// <c>CREATE TABLE</c> and commits it (<c>RunCommandTransaction</c>), and readers that open a
    /// connection per <c>SELECT count(*)</c> (<c>RunCommand</c>) against tables the writer has
    /// <b>already committed</b>. Connection pooling is on, which is what the framework's connection
    /// string gets by default — it emits only <c>Data Source</c>, an optional <c>Password</c> and
    /// <c>Default Timeout</c>.</para>
    ///
    /// <para>What it prints for each failure is the datum the whole investigation was missing: the
    /// <c>PRAGMA schema_version</c> of the connection that <b>just failed</b>, beside the value an
    /// independent connection reports at the same moment. A cookie that is <b>behind</b> is the stale
    /// schema image, stated as a measurement instead of inferred from a symptom.</para>
    ///
    /// <para>⚠ This is a diagnostic, not a guard: it is load-dependent and gated on <c>BIRKO_STORM</c>.
    /// The framework-level reproduction and its pooled/unpooled control live in
    /// <c>ColdTableStormTests</c>.</para>
    /// </summary>
    [Fact]
    public async Task APooledConnectionCanAnswerFromAStaleSchemaImage()
    {
        if (!RequireStorm(_out)) return;

        var cs = Cs("rawpool.db");
        using (var seed = new SqliteConnection(cs))
        {
            seed.Open();
            using var cmd = seed.CreateCommand();
            cmd.CommandText = "CREATE TABLE Anchor (Id INTEGER)";
            cmd.ExecuteNonQuery();
        }

        var created = 0;                 // tables the writer has COMMITTED, monotonic
        var stop = false;
        var findings = new List<string>();
        var sync = new object();

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 200 && !stop; i++)
            {
                // The framework's shape: a connection and a transaction per DDL statement.
                using var db = new SqliteConnection(cs);
                db.Open();
                using (var tx = db.BeginTransaction())
                using (var cmd = db.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = $"CREATE TABLE IF NOT EXISTS T{i:000} (Id INTEGER)";
                    cmd.ExecuteNonQuery();
                    tx.Commit();
                }
                db.Close();
                Volatile.Write(ref created, i + 1);
            }
        });

        var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
                        while (!Volatile.Read(ref stop))
            {
                var ceiling = Volatile.Read(ref created);
                if (ceiling == 0) continue;
                // The NEWEST table the writer has already committed. That is the framework's shape: a
                // caller released from another caller's _initLock immediately counts the table that init
                // just created, so the create is milliseconds old — which is the whole window. Targeting a
                // random older table found nothing in 200 creates, because a pooled handle gets many
                // chances to refresh in between.
                var target = $"T{ceiling - 1:000}";

                using var db = new SqliteConnection(cs);
                db.Open();
                try
                {
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = $"SELECT count(*) FROM \"{target}\"";
                    cmd.ExecuteScalar();
                }
                catch (SqliteException ex)
                {
                    // The decisive read: this connection is STILL OPEN, so its own cookie is available.
                    long onFailing = -1;
                    try { onFailing = SchemaVersion(db); } catch { }
                    long independent = -1;
                    var presentNow = false;
                    try
                    {
                        using var other = new SqliteConnection(cs);
                        other.Open();
                        independent = SchemaVersion(other);
                        presentNow = Scalar(other,
                            $"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='{target}'") == "1";
                    }
                    catch { }

                    lock (sync)
                    {
                        findings.Add($"{target}: code={ex.SqliteErrorCode} "
                            + $"cookieOnFailingConnection={onFailing} cookieIndependent={independent} "
                            + $"presentNow={presentNow} committedCeiling={ceiling} :: {ex.Message}");
                    }
                }
                db.Close();
            }
        })).ToArray();

        await writer;
        Volatile.Write(ref stop, true);
        await Task.WhenAll(readers);

        _out.WriteLine($"committed tables: {Volatile.Read(ref created)}; failures: {findings.Count}");
        foreach (var f in findings.Take(12))
        {
            _out.WriteLine("  " + f);
        }

        // No assertion on the COUNT of failures: this is a race and a quiet run proves nothing either way.
        // What is asserted is the classification of whatever did fail, because that is the finding.
        foreach (var f in findings)
        {
            f.Should().Contain("presentNow=True",
                "every failure here must be against a table that IS in the file — otherwise this probe is "
                + "measuring a missing table rather than a stale view of a present one");
        }
    }
}
