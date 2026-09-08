using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-290 — the framework-level reproduction of the schema-ensure escape: <b>a statement reports a table
/// missing that this connector created and committed, while the store's init gate had passed.</b>
///
/// <para><b>The condition, measured on the consumer side rather than guessed here.</b> The trigger is
/// LOAD, not timing: ~190 <i>different</i> cold tables first-touched concurrently against one connector
/// and one database file. Same-table concurrency reproduces nothing (it serialises on that store's
/// <c>_initLock</c>), and an idle API reproduces nothing (cold schema-ensure costs 1-5 ms there, so there
/// is no window to aim at). This class builds that shape without a host.</para>
///
/// <para>⚠ <b>Scoring is on the RECORD, never on a thrown exception or a log line.</b>
/// <c>AbstractConnector.SchemaEscapes</c> is the truth; TASK-285 answers a missing table on a count with
/// <c>0</c>, so an escape serves a silently wrong answer and <b>throws nothing</b>. A storm that asserted
/// "no exception" would pass against the defect on every run.</para>
///
/// <para>⚠ <b>A zero result is worthless without the positive control below.</b> The channel is
/// transition-fired per (statement, table set) and discriminates on TASK-286's annotation, so there are
/// several ways for it to be silent while the condition happens. <see cref="ThePositiveControl"/> forces
/// one escape in this exact fixture, so "the storm recorded none" means the instrument was live.</para>
/// </summary>
public class ColdTableStormTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;
    private static int _seq;

    public ColdTableStormTests(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(), $"birko-storm-{Guid.NewGuid():N}");
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

    /// <summary>
    /// A database of its own per test. Connectors are cached process-wide per (type, settings id) and
    /// TASK-288's trust check compares that connector's <c>SchemaGeneration</c>, so a shared connector
    /// would let one test's escape change what the next one measures.
    /// </summary>
    private SqLiteSettings Fresh() => new SqLiteSettings(_root, $"storm{Interlocked.Increment(ref _seq)}.db");

    /// <summary>
    /// The same, on an explicit journal mode. <c>"DELETE"</c> is SQLite's own default and what this
    /// framework left in place until TASK-296 — i.e. the configuration the defect lives on.
    /// </summary>
    private SqLiteSettings Fresh(string journalMode)
        => new SqLiteSettings(_root, $"storm{Interlocked.Increment(ref _seq)}.db") { JournalMode = journalMode };

    /// <summary>
    /// The two storm tests are <b>opt-in</b>, and a skipped run says so out loud — same idiom as the
    /// live provider suites.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>They are a diagnostic, not a guard, and the reason is measured rather than stylistic.</b>
    /// Each one drives 200 concurrent first touches against a single SQLite file for ~33 s, which
    /// saturates the disk for every test xUnit runs beside it — measured: it made
    /// <c>PerStoreDoorResidueTests</c> fail with <c>SQLITE_BUSY</c> at <c>BeginTransaction</c>, in a test
    /// about visibility that expects no contention whatever. A diagnostic that reds its neighbours is
    /// worse than one nobody runs by default.
    /// <para>
    /// <see cref="ThePositiveControl"/> is NOT gated: it is 250 ms, and it is the thing that says whether
    /// the escape channel is live in this fixture at all.
    /// </para>
    /// </remarks>
    private bool RequireStorm()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_STORM")))
        {
            return true;
        }
        _out.WriteLine("SKIPPED: set BIRKO_STORM to run the cold-table storm (~35 s, saturates the disk).");
        return false;
    }

    private static SqLiteConnector Connector(SqLiteSettings settings)
        => (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(settings);

    // ── the probe: one store per entity type, reached without knowing the type at compile time ───────

    /// <summary>
    /// One store per probe type, and the two operations a consumer's GET list route performs: a
    /// <c>COUNT</c> and a <c>SELECT</c>. Both are first touches, so either can be the one that
    /// schema-ensures.
    /// </summary>
    private static Probe MakeProbe(Type type, SqLiteSettings settings)
        => (Probe)typeof(ColdTableStormTests)
            .GetMethod(nameof(BuildProbe), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type)
            .Invoke(null, new object[] { settings })!;

    private static Probe BuildProbe<T>(SqLiteSettings settings) where T : Birko.Data.Models.AbstractModel, new()
    {
        // ONE store per type, shared by every concurrent caller of that type — the production shape. A
        // store per call would give each caller its own _initialized flag and its own _initLock, which is
        // not what a web app with singleton stores does.
        var store = new AsyncSQLiteStore<T>();
        store.SetSettings(settings);
        return new Probe(
            typeof(T).Name,
            async ct =>
            {
                var count = await store.CountAsync(null, ct).ConfigureAwait(false);
                var rows = await store.ReadAsync(null, null, null, null, ct).ConfigureAwait(false);
                return count + (rows?.Count() ?? 0);
            },
            async ct =>
            {
                await store.CreateAsync(new T { Guid = Guid.NewGuid() }, null, ct).ConfigureAwait(false);
            });
    }

    private sealed record Probe(string Name, Func<CancellationToken, Task<long>> Read, Func<CancellationToken, Task> Write);

    /// <summary>
    /// Releases every probe at once. Without this the thread pool injects blocking-bound threads at
    /// roughly one or two per second, and 200 "concurrent" first touches degrade into a queue — which is
    /// precisely the condition measured NOT to reproduce.
    /// </summary>
    private static async Task<List<Exception>> Storm(IEnumerable<Func<CancellationToken, Task>> work)
    {
        ThreadPool.GetMinThreads(out var workerMin, out var ioMin);
        ThreadPool.SetMinThreads(Math.Max(workerMin, 512), ioMin);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        var sync = new object();
        try
        {
            var tasks = work.Select(w => Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                try { await w(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { lock (sync) { failures.Add(ex); } }
            })).ToArray();

            start.SetResult();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            ThreadPool.SetMinThreads(workerMin, ioMin);
        }
        return failures;
    }

    private void Report(SqLiteConnector connector, IReadOnlyCollection<Exception> failures, string label)
    {
        _out.WriteLine($"[{label}] escapes={connector.SchemaEscapes.Count} "
            + $"generation={connector.SchemaGeneration} created={connector.TablesCreated.Count} "
            + $"failures={failures.Count} indexFailures={connector.IndexCreationFailures.Count}");
        foreach (var escape in connector.SchemaEscapes)
        {
            _out.WriteLine($"  ESCAPE [{string.Join(", ", escape.TableNames)}] {escape.Annotation}");
        }
        // The CHAIN, not the outermost message. EnsureSchemaAndReport rethrows as
        // `new Exception(commandText, ex)`, so the top-level message is the SQL and the provider's own
        // error — the only thing that says WHICH failure this was — is one level down. Classifying on the
        // outer message here would have reported every one of these as an unknown failure.
        foreach (var group in failures.GroupBy(x => Chain(connector, x)))
        {
            _out.WriteLine($"  FAIL x{group.Count()} {group.Key}");
        }
    }

    private static string Chain(SqLiteConnector connector, Exception ex)
    {
        var parts = new List<string>();
        for (var current = (Exception?)ex; current != null; current = current.InnerException)
        {
            var code = current is SqliteException sq ? $"[sqlite {sq.SqliteErrorCode}]" : "";
            parts.Add($"{current.GetType().Name}{code}: {Trim(current.Message)}");
        }
        var missing = connector.IsMissingTableExceptionChain(ex) ? "MISSING-TABLE " : "";
        var anomalous = connector.IsAnomalousSchemaEscapeChain(ex) ? "ANOMALOUS " : "";
        return missing + anomalous + string.Join(" <- ", parts);
    }

    private static string Trim(string? message)
        => message == null ? "(null)" : (message.Length <= 120 ? message : message[..120]);

    // ── the reproduction ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheStorm_ConcurrentFirstTouchOfManyColdTables_RecordsNoSchemaEscape()
    {
        if (!RequireStorm()) return;
        var settings = Fresh();
        var connector = Connector(settings);
        var probes = ColdTableProbes.All.Select(t => MakeProbe(t, settings)).ToArray();

        var failures = await Storm(probes.Select(p => (Func<CancellationToken, Task>)(async ct => await p.Read(ct))));

        Report(connector, failures, "read-storm");
        connector.TablesCreated.Should().HaveCount(probes.Length,
            "every probe's store must actually have schema-ensured, or the storm never reached the "
            + "condition and a zero escape count says nothing about it");
        connector.SchemaEscapes.Should().BeEmpty();
    }

    [Fact]
    public async Task TheStorm_WithWritesMixedIn_RecordsNoSchemaEscape()
    {
        if (!RequireStorm()) return;
        var settings = Fresh();
        var connector = Connector(settings);
        var probes = ColdTableProbes.All.Select(t => MakeProbe(t, settings)).ToArray();

        // Writes reach EnsureSchemaAndReport and THROW (TASK-277), so this half is loud where the read
        // half is silent. Interleaving them is also what puts a write lock on the file while other
        // stores are still schema-ensuring.
        var work = new List<Func<CancellationToken, Task>>();
        for (var i = 0; i < probes.Length; i++)
        {
            var probe = probes[i];
            work.Add(async ct => await probe.Read(ct));
            if (i % 3 == 0) work.Add(probe.Write);
        }

        var failures = await Storm(work);

        Report(connector, failures, "mixed-storm");
        connector.SchemaEscapes.Should().BeEmpty();
    }

    // ── the positive control, without which a zero above is unfalsifiable ───────────────────────────

    [Fact]
    public async Task ThePositiveControl()
    {
        var settings = Fresh();
        var connector = Connector(settings);
        var probe = MakeProbe(typeof(Probe000), settings);

        await probe.Write(CancellationToken.None);
        connector.TablesCreated.Keys.Should().Contain("Probe000");

        // The known-good way to produce the condition, from TASK-288: the table vanishes beneath a store
        // that has already schema-ensured. This is NOT the mechanism under investigation — it stands in
        // for it, to prove the instrument in this fixture is live.
        connector.DropTable(new[] { typeof(Probe000) });
        await probe.Read(CancellationToken.None);

        Report(connector, Array.Empty<Exception>(), "control");
        connector.SchemaEscapes.Should().ContainSingle(
            "if this is empty the storm tests above are measuring nothing — the channel discriminates on "
            + "TASK-286's annotation and is transition-fired, so there are several ways for it to be "
            + "silent while the condition is happening")
            .Which.TableNames.Should().Contain("Probe000");
    }


    /// <summary>
    /// What an <b>independent</b> connection sees at the exact moment an escape is detected.
    /// </summary>
    /// <remarks>
    /// This is the measurement that separates TASK-290's two remaining families, and it needs no framework
    /// plumbing at all: <c>OnSchemaEscapeDetected</c> is raised <b>synchronously</b> from inside
    /// <c>EnsureSchemaAndReport</c>, so a handler runs while the failing statement's flow is still on the
    /// stack. A handler that then opens its own connection and asks <c>sqlite_master</c> answers the only
    /// question left:
    /// <list type="bullet">
    /// <item>the table is <b>absent</b> from the file → something removed it, or it was never durably
    /// there. A visibility story is dead;</item>
    /// <item>the table is <b>present</b> → the failing statement read an image that did not contain it,
    /// i.e. a visibility effect, and <c>schema_version</c> says how far behind.</item>
    /// </list>
    /// <para>Safe by construction since TASK-289: a throwing handler is isolated and recorded rather than
    /// replacing the exception in flight.</para>
    /// </remarks>
    private sealed record EscapeObservation(
        string Tables, bool TablePresentNow, long SchemaVersionNow, int TablesInFileNow);

    private List<EscapeObservation> ObserveEscapes(SqLiteConnector connector, SqLiteSettings settings)
    {
        var seen = new List<EscapeObservation>();
        var sync = new object();
        connector.OnSchemaEscapeDetected += escape =>
        {
            var name = escape.TableNames.FirstOrDefault() ?? "(none)";
            using var db = new SqliteConnection(settings.GetConnectionString());
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT (SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$n), "
                + "(SELECT count(*) FROM sqlite_master WHERE type='table')";
            cmd.Parameters.AddWithValue("$n", name);
            using var reader = cmd.ExecuteReader();
            reader.Read();
            var present = reader.GetInt32(0) > 0;
            var total = reader.GetInt32(1);
            reader.Close();
            using var pragma = db.CreateCommand();
            pragma.CommandText = "PRAGMA schema_version";
            var version = Convert.ToInt64(pragma.ExecuteScalar());
            lock (sync)
            {
                seen.Add(new EscapeObservation(string.Join(",", escape.TableNames), present, version, total));
            }
        };
        return seen;
    }

    // ───────────── TASK-290 Round 2: the two shapes Round 1's probes left open ─────────────

    /// <summary>
    /// <b>An open boundary holds an UNCOMMITTED create, which Round 1 measured reads as
    /// <c>no such table</c> with no lock error — so this is the one interleaving that could produce the
    /// anomaly without anything being removed.</b>
    ///
    /// <para>Flow A opens a <c>SqlUnitOfWork</c>, first-touches the entity (so the <c>CREATE TABLE</c>
    /// runs on A's connection inside A's transaction and <c>TablesCreated</c> records it immediately) and
    /// then <b>holds</b>. Flow B counts the same table on the same singleton store, concurrently.</para>
    ///
    /// <para>Round 1 argued from the code that B cannot see the uncommitted image, because B's own
    /// schema-ensure has to take SQLite's write lock first and will block on A. This measures it instead —
    /// the discipline this task exists to apply, since thirteen hypotheses have already died by code
    /// reading. Whatever B does, the outcome is printed and the escape count asserted.</para>
    /// </summary>
    [Fact]
    public async Task AnOpenBoundaryHoldingAnUncommittedCreate_DoesNotProduceAnAnomalousEscape()
    {
        var settings = new SqLiteSettings(_root, $"boundary{Interlocked.Increment(ref _seq)}.db")
        {
            // Short, so a blocked reader fails fast and visibly instead of hiding inside the default 30 s.
            CommandTimeout = 3,
        };
        var connector = Connector(settings);
        var store = new AsyncSQLiteStore<Probe000>();
        store.SetSettings(settings);

        var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var holder = Task.Run(async () =>
        {
            await using var uow = SqlUnitOfWork.FromStore(store);
            await uow.BeginAsync();
            // First touch INSIDE the boundary: the DDL joins A's transaction and is not committed.
            await store.CreateAsync(new Probe000 { Guid = Guid.NewGuid() });
            created.SetResult();
            await release.Task;
            await uow.RollbackAsync();
        });

        await created.Task;
        connector.TablesCreated.Keys.Should().Contain("Probe000",
            "the create is recorded the moment the statement runs, not when the boundary commits — which "
            + "is what makes this interleaving a candidate at all");

        long count = -1;
        Exception? failure = null;
        var reader = Task.Run(async () =>
        {
            try { count = await store.CountAsync(); }
            catch (Exception ex) { failure = ex; }
        });

        await reader;
        release.SetResult();
        await holder;

        _out.WriteLine($"[boundary] reader count={count} "
            + $"failure={failure?.GetType().Name}: {Trim(failure?.Message)}");
        if (failure != null)
        {
            _out.WriteLine($"           chain: {Chain(connector, failure)}");
        }
        Report(connector, failure == null ? Array.Empty<Exception>() : new[] { failure }, "boundary");

        connector.SchemaEscapes.Should().BeEmpty(
            "a concurrent reader cannot reach the uncommitted image: its own schema-ensure has to take "
            + "SQLite's write lock first and blocks on the boundary holder, so it either waits or reports "
            + "SQLITE_BUSY — never 'no such table' for a table this connector recorded");
    }

    /// <summary>
    /// <b>The tuned storm.</b> Round 1's variants saturated the command timeout — 6-7 <c>SQLite Error 5</c>
    /// per run, each after roughly 30 s of waiting — which is strictly more contended than the condition
    /// being chased: the consumer measured <c>Error 5 = 0</c> across five cycles, so its contention never
    /// exceeded that ceiling.
    ///
    /// <para>So this one is shaped the other way: smaller <b>waves</b> with several concurrent callers per
    /// table, repeated, so schema-ensure and counts interleave without piling up. Multiple callers per
    /// table matter because that is the consumer's actual profile — three concurrent
    /// <c>GET /movement-codes</c> in the cycle that produced its clearest evidence — and because a caller
    /// that waits on another's <c>_initLock</c> proceeds to its statement the instant that init returns.</para>
    /// </summary>
    [Fact]
    public async Task TheTunedStorm_SmallerWavesWithSeveralCallersPerTable()
    {
        if (!RequireStorm()) return;
        var settings = Fresh();
        var connector = Connector(settings);
        var probes = ColdTableProbes.All.Select(t => MakeProbe(t, settings)).ToArray();
        var observed = ObserveEscapes(connector, settings);

        const int waveSize = 24;      // tables per wave
        const int callersPerTable = 3;
        var failures = new List<Exception>();

        for (var offset = 0; offset < probes.Length; offset += waveSize)
        {
            var wave = probes.Skip(offset).Take(waveSize).ToArray();
            var work = new List<Func<CancellationToken, Task>>();
            foreach (var probe in wave)
            {
                for (var c = 0; c < callersPerTable; c++)
                {
                    work.Add(async ct => await probe.Read(ct));
                }
            }
            failures.AddRange(await Storm(work));
        }

        Report(connector, failures, $"tuned-storm waves of {waveSize}x{callersPerTable}");
        foreach (var o in observed)
        {
            _out.WriteLine($"  OBSERVED [{o.Tables}] presentNow={o.TablePresentNow} "
                + $"schemaVersionNow={o.SchemaVersionNow} tablesInFileNow={o.TablesInFileNow}");
        }

        connector.TablesCreated.Should().HaveCount(probes.Length,
            "every probe must have schema-ensured, or the storm never reached the condition");

        // TASK-296 — this is the FIX's proof, and it is only worth anything because the identical shape
        // on the rollback journal still fires: see TheTunedStorm_OnTheRollbackJournal_IsWhereTheDefectLives,
        // which is what stops this 0 being a broken reproduction. Run the two together.
        connector.SchemaEscapes.Should().BeEmpty(
            "on the shipped default (WAL) the stale-schema-image read does not happen. Measured: this "
            + "exact shape fired on 7 of 7 runs with 2-9 escapes each while the framework left SQLite on "
            + "its rollback journal, and 0 of 3 after");
        observed.Should().BeEmpty();
    }

    /// <summary>
    /// <b>TASK-296 — the defect, still reproducible on the rollback journal, which is what makes the
    /// WAL result above mean something.</b>
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The escape COUNT is deliberately not asserted: this is a race.</b> A quiet run would prove
    /// nothing either way, and a <c>&gt;= 1</c> assertion would be a flake waiting for a faster machine.
    /// What is asserted is the classification of whatever fires — the table is <b>in the file</b>,
    /// observed synchronously from an independent connection, so nothing removed it and the failing
    /// statement read a stale image. The claim that it fires at all is the recorded 7-of-7 (2-9 escapes
    /// each) in TASK-290's Round 2 section.
    /// </remarks>
    [Fact]
    public async Task TheTunedStorm_OnTheRollbackJournal_IsWhereTheDefectLives()
    {
        if (!RequireStorm()) return;
        var settings = Fresh("DELETE");
        var connector = Connector(settings);
        var probes = ColdTableProbes.All.Select(t => MakeProbe(t, settings)).ToArray();
        var observed = ObserveEscapes(connector, settings);

        const int waveSize = 24;
        const int callersPerTable = 3;
        var failures = new List<Exception>();

        for (var offset = 0; offset < probes.Length; offset += waveSize)
        {
            var wave = probes.Skip(offset).Take(waveSize).ToArray();
            var work = new List<Func<CancellationToken, Task>>();
            foreach (var probe in wave)
            {
                for (var c = 0; c < callersPerTable; c++)
                {
                    work.Add(async ct => await probe.Read(ct));
                }
            }
            failures.AddRange(await Storm(work));
        }

        Report(connector, failures, $"rollback-journal waves of {waveSize}x{callersPerTable}");
        foreach (var o in observed)
        {
            _out.WriteLine($"  OBSERVED [{o.Tables}] presentNow={o.TablePresentNow} "
                + $"schemaVersionNow={o.SchemaVersionNow} tablesInFileNow={o.TablesInFileNow}");
        }

        connector.TablesCreated.Should().HaveCount(probes.Length);
        observed.Should().OnlyContain(o => o.TablePresentNow,
            "an escape against a table that is genuinely absent would mean this probe is measuring "
            + "something else entirely");
    }

    /// <summary>
    /// <c>SqLiteSettings.GetConnectionString()</c> is virtual, which makes the decisive experiment
    /// possible with no framework change: the same storm, on a connection string that disables
    /// <b>connection pooling</b>.
    /// </summary>
    /// <remarks>
    /// The framework emits only <c>Data Source</c>, an optional <c>Password</c> and
    /// <c>Default Timeout</c>, so pooling is on — Microsoft.Data.Sqlite pools by default, and a pooled
    /// handle keeps its <c>sqlite3</c> connection alive across <c>Close()</c>, including its page and
    /// schema caches. Every framework read and write opens and closes a connection per statement
    /// (<c>RunCommand</c>, <c>RunCommandTransaction</c>), so under load the same handles circulate.
    /// </remarks>
    private sealed class UnpooledSqLiteSettings : SqLiteSettings
    {
        public UnpooledSqLiteSettings(string location, string name) : base(location, name) { }

        public override string GetConnectionString() => base.GetConnectionString() + ";Pooling=False";
    }

    /// <summary>
    /// The same tuned storm with pooling <b>off</b>. If the escapes vanish, the mechanism is named: a
    /// pooled <c>sqlite3</c> handle answering a statement from a schema image older than a create another
    /// connection had already committed. If they persist, pooling is not it and the next probe is the
    /// failing connection's own <c>PRAGMA schema_version</c>.
    /// </summary>
    [Fact]
    public async Task TheTunedStorm_WithConnectionPoolingDisabled()
    {
        if (!RequireStorm()) return;
        // On the ROLLBACK JOURNAL deliberately: with WAL the defect is gone anyway, so a WAL+unpooled
        // run would say nothing about pooling. This isolates the one variable it is about.
        var settings = new UnpooledSqLiteSettings(_root, $"unpooled{Interlocked.Increment(ref _seq)}.db")
        {
            JournalMode = "DELETE",
        };
        var connector = Connector(settings);
        var probes = ColdTableProbes.All.Select(t => MakeProbe(t, settings)).ToArray();
        var observed = ObserveEscapes(connector, settings);

        const int waveSize = 24;
        const int callersPerTable = 3;
        var failures = new List<Exception>();

        for (var offset = 0; offset < probes.Length; offset += waveSize)
        {
            var wave = probes.Skip(offset).Take(waveSize).ToArray();
            var work = new List<Func<CancellationToken, Task>>();
            foreach (var probe in wave)
            {
                for (var c = 0; c < callersPerTable; c++)
                {
                    work.Add(async ct => await probe.Read(ct));
                }
            }
            failures.AddRange(await Storm(work));
        }

        Report(connector, failures, $"unpooled waves of {waveSize}x{callersPerTable}");
        foreach (var o in observed)
        {
            _out.WriteLine($"  OBSERVED [{o.Tables}] presentNow={o.TablePresentNow} "
                + $"schemaVersionNow={o.SchemaVersionNow} tablesInFileNow={o.TablesInFileNow}");
        }

        connector.TablesCreated.Should().HaveCount(probes.Length,
            "every probe must have schema-ensured, or this variant is not comparable with the pooled one");
        connector.SchemaEscapes.Should().BeEmpty(
            "THE CONTROL that named the mechanism (TASK-290): the only difference from the rollback-journal "
            + "variant is `Pooling=False`. Measured: pooled 7 of 7 runs with 2-9 escapes each, unpooled "
            + "0 of 4. ⚠ It is NOT the remedy TASK-296 chose — it is 1.5x SLOWER in the warm sequential "
            + "case (2,731 ms against 1,801 ms for 200 write+count+read cycles) while WAL is 5x faster. "
            + "The 2.4x speedup it shows under the storm is a contention artefact");
        observed.Should().BeEmpty();
    }

}
