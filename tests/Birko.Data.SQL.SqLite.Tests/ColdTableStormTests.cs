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
    /// ⚠ <b>No <c>SqliteConnection.ClearAllPools()</c> here, deliberately — measured.</b> Twenty-four of
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
}
