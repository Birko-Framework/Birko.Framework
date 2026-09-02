using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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
/// TASK-291 + TASK-294 — <b>what the blanket rewrap in <c>EnsureSchemaAndReport</c> destroys.</b>
///
/// <para>Every provider's <c>OnException</c> handler funnels into it, for <b>every</b> exception, and it
/// throws <c>new Exception(DescribeSchemaEscape(ex, commandText), ex)</c>. For a missing table that is
/// the point — TASK-286's annotation rides on the message deliberately, because a consumer's boot-time
/// checks cannot see an event. For everything else <c>DescribeSchemaEscape</c> returns the command text
/// unchanged, so the rewrap adds the SQL and <b>loses the exception's type</b>.</para>
///
/// <para>Three things depend on that type, and they are why these two tasks are one change:</para>
/// <list type="number">
/// <item><b>The retry policy.</b> <c>ExecuteWithRetry</c> filters on
/// <c>IsTransientException(ex)</c> — the <i>direct</i> predicate, not a chain walk — so a
/// <c>SQLITE_BUSY</c> that has been rewrapped is no longer transient and is never retried.</item>
/// <item><b>Cancellation.</b> A client that hung up produces an <c>OperationCanceledException</c>, which
/// a host maps to an abort rather than a fault — unless it arrives as a bare <c>Exception</c>.</item>
/// <item><b>Anything a host catches by type</b>, which is § TASK-289's lesson in reverse: an exception
/// filter is a silent coupling, and replacing an exception switches one off from a distance.</item>
/// </list>
/// </summary>
public class RewrapClassificationTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;
    private static int _seq;

    public RewrapClassificationTests(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(), $"birko-rewrap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    /// <remarks>No process-wide <c>ClearAllPools()</c> — see [[TASK-276]].</remarks>
    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("RewrapRows")]
    public class RewrapRow : AbstractDatabaseModel
    {
        public string? Value { get; set; }
    }

    /// <summary>
    /// A real <c>SQLITE_BUSY</c>, constructed rather than provoked — the classification is what is under
    /// test here, and provoking one belongs to the end-to-end case below.
    /// </summary>
    private static SqliteException MakeBusy()
        => new("SQLite Error 5: 'database is locked'.", 5);

    /// <summary>Exposes the protected funnel so what it does to each exception shape is measurable.</summary>
    private sealed class Probe : AbstractConnector
    {
        public Probe() : base(new Birko.Configuration.PasswordSettings()) { }

        public Exception? Report(Exception ex, string? commandText)
        {
            try { EnsureSchemaAndReport(ex, commandText); return null; }
            catch (Exception thrown) { return thrown; }
        }

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(DbType type, Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }

    // ── what comes out, per exception shape ─────────────────────────────────────────────────────────

    [Fact]
    public void A_cancellation_keeps_its_type_and_records_nothing()
    {
        var probe = new Probe();

        var thrown = probe.Report(new OperationCanceledException("client went away"), "SELECT 1");

        _out.WriteLine($"cancellation -> {thrown?.GetType().Name}: {thrown?.Message}");
        thrown.Should().BeAssignableTo<OperationCanceledException>(
            "TASK-291: a cancellation is the caller's own decision, already reported to it by its own "
            + "token. Rewrapping it as a bare Exception turns a client that hung up into a 500 and loses "
            + "the distinction a host needs to tell an abort from a fault");
        probe.SchemaEscapes.Should().BeEmpty();
        probe.SchemaGeneration.Should().Be(0, "nothing about a cancellation is a schema anomaly");
    }

    [Fact]
    public void A_transient_failure_keeps_its_type_so_the_retry_policy_can_still_see_it()
    {
        var probe = new Probe();
        var busy = MakeBusy();

        var thrown = probe.Report(busy, "SELECT count(*) FROM \"RewrapRows\"");

        _out.WriteLine($"busy -> {thrown?.GetType().Name}: {thrown?.Message}");
        thrown.Should().BeOfType<SqliteException>()
            .Which.SqliteErrorCode.Should().Be(5, "the provider's own exception, unchanged");

        // ⚠ Asked of the SQLITE connector's predicate, not the bare base one. The base tests
        // `ex is DbException { IsTransient: true }`, which Microsoft.Data.Sqlite does not set for code 5 —
        // measured, when an earlier version of this test asked the wrong connector. What runs in
        // production is SqLiteConnector's override, which classifies 5 and 6 explicitly, and that is what
        // ExecuteWithRetry consults.
        var real = (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(
            new SqLiteSettings(_root, $"transient{Interlocked.Increment(ref _seq)}.db"));
        real.IsTransientException(thrown!).Should().BeTrue(
            "TASK-294: ExecuteWithRetry filters on the DIRECT predicate, not a chain walk, so while this "
            + "was rewrapped as a bare Exception it stopped being transient and the RetryPolicy a "
            + "consumer configured silently never fired");
        probe.SchemaEscapes.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_table_is_still_rewrapped_with_the_annotation()
    {
        var probe = new Probe();
        var missing = new InvalidOperationException("SQLite Error 1: 'no such table: RewrapRows'.");

        var thrown = probe.Report(missing, "SELECT count(*) FROM \"RewrapRows\"");

        _out.WriteLine($"missing -> {thrown?.GetType().Name}: {thrown?.Message}");
        thrown.Should().NotBeNull();
        thrown!.Message.Should().Contain("SELECT count(*)",
            "TASK-286's annotation rides on the message deliberately — a consumer's boot-time checks "
            + "cannot see an event, so this is the one shape where the rewrap earns its cost");
        thrown.Message.Should().Contain("schema-ensure escape");
        thrown.InnerException.Should().BeSameAs(missing);
        probe.IsMissingTableExceptionChain(thrown).Should().BeTrue(
            "TASK-285's count catch selects on the chain, so it must still match");
    }

    /// <summary>
    /// The command text is diagnostically valuable and is what the rewrap was really adding for a
    /// non-missing-table failure. Preserving the type must not throw it away.
    /// </summary>
    [Fact]
    public void The_command_text_survives_on_an_exception_whose_type_is_preserved()
    {
        var probe = new Probe();

        var thrown = probe.Report(MakeBusy(), "SELECT count(*) FROM \"RewrapRows\"");

        thrown!.Data.Contains(AbstractConnector.CommandTextDataKey).Should().BeTrue(
            "losing the statement would trade one diagnostic for another; Exception.Data carries it "
            + "without touching the type");
        Convert.ToString(thrown.Data[AbstractConnector.CommandTextDataKey]).Should().Contain("RewrapRows");
    }

    // ── and end to end, where it actually costs something ──────────────────────────────────────────

    /// <summary>
    /// <b>The contrast that explains the asymmetry, and it corrected this suite's first design.</b>
    ///
    /// <para>A lock taken <i>before</i> the statement — <c>RunCommandTransaction</c> opens the connection
    /// and calls <c>BeginTransaction()</c> <b>outside</b> its <c>try</c>, and Microsoft.Data.Sqlite issues
    /// <c>BEGIN IMMEDIATE</c> for its default isolation level, which takes the write lock at once — never
    /// reaches <c>InitException</c> at all. So it arrives as a real <c>SqliteException</c>,
    /// <c>ExecuteWithRetry</c>'s filter matches it, and the retry policy works.</para>
    ///
    /// <para>Measured: <b>0</b> <c>OnExecute</c> invocations for the INSERT (the command was never built)
    /// and a raw <c>SqliteException</c> code 5 surfacing after the retries. That is what a preserved type
    /// buys, on the one path that already had it — and it is why the funnel-level tests above are the
    /// defect: a failure raised <i>inside</i> the try was classified out of existence before any filter
    /// saw it.</para>
    /// </summary>
    /// <remarks>
    /// The database is deliberately on the <b>rollback journal</b>: WAL admits readers and does not
    /// serialise this way, so it is the wrong instrument for lock contention. That is also TASK-296's
    /// measured consequence — on the shipped default this contention is largely gone, which lowers how
    /// often the defect is reachable without making it any less wrong.
    /// </remarks>
    [Fact]
    public async Task A_lock_taken_before_the_statement_already_surfaces_with_its_type()
    {
        var settings = new SqLiteSettings(_root, $"rewrap{Interlocked.Increment(ref _seq)}.db")
        {
            JournalMode = "DELETE",
            CommandTimeout = 1,
        };
        var store = new AsyncSQLiteStore<RewrapRow>();
        store.SetSettings(settings);
        await store.CreateAsync(new RewrapRow { Guid = Guid.NewGuid(), Value = "seed" });

        var connector = (SqLiteConnector)DataBase.GetConnector<SqLiteConnector>(settings);
        connector.RetryPolicy = new RetryPolicy
        {
            MaxRetries = 2,
            BaseDelay = TimeSpan.FromMilliseconds(20),
            UseExponentialBackoff = false,
        };

        var executed = new List<string>();
        connector.OnExecute += sql => { lock (executed) { executed.Add(sql); } };

        using var blocker = new SqliteConnection(settings.GetConnectionString());
        await blocker.OpenAsync();
        using var hold = blocker.BeginTransaction();
        using (var cmd = blocker.CreateCommand())
        {
            cmd.Transaction = hold;
            cmd.CommandText = "CREATE TABLE Blocker (Id INTEGER)";
            cmd.ExecuteNonQuery();
        }

        Exception? failure = null;
        try { await store.CreateAsync(new RewrapRow { Guid = Guid.NewGuid(), Value = "blocked" }); }
        catch (Exception ex) { failure = ex; }

        hold.Rollback();

        var inserts = executed.FindAll(s => s.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase));
        _out.WriteLine($"INSERT statements built: {inserts.Count}; surfaced: {failure?.GetType().Name}: "
            + $"{failure?.Message}");

        inserts.Should().BeEmpty(
            "the lock is taken by BEGIN IMMEDIATE, before the command is ever built — which is how this "
            + "path stays outside the try and therefore outside the rewrap");
        failure.Should().BeOfType<SqliteException>()
            .Which.SqliteErrorCode.Should().Be(5,
                "this path already surfaces the provider's own exception, so a host can map it and "
                + "ExecuteWithRetry can retry it. The funnel-level tests above are about the paths that "
                + "could not");
    }

    /// <summary>
    /// <b>End to end, through a real provider path, with a real provider exception raised INSIDE the
    /// try</b> — which is the shape the funnel probe above cannot produce.
    ///
    /// <para>A duplicate primary key is a genuine <c>SqliteException</c> (constraint violation, code 19)
    /// thrown by <c>ExecuteNonQuery</c> inside <c>RunCommandTransaction</c>'s <c>try</c>, so it goes
    /// through <c>InitException</c> → the provider's <c>OnException</c> → <c>EnsureSchemaAndReport</c>.
    /// Before this change it reached the caller as a bare <c>Exception</c> whose message was the SQL.</para>
    /// </summary>
    [Fact]
    public async Task A_real_constraint_violation_reaches_the_caller_as_itself()
    {
        var settings = new SqLiteSettings(_root, $"rewrap{Interlocked.Increment(ref _seq)}.db");
        var store = new AsyncSQLiteStore<RewrapRow>();
        store.SetSettings(settings);

        var guid = Guid.NewGuid();
        await store.CreateAsync(new RewrapRow { Guid = guid, Value = "first" });

        Exception? failure = null;
        try { await store.CreateAsync(new RewrapRow { Guid = guid, Value = "duplicate" }); }
        catch (Exception ex) { failure = ex; }

        _out.WriteLine($"duplicate key -> {failure?.GetType().Name}: {failure?.Message}");
        _out.WriteLine($"  data[{AbstractConnector.CommandTextDataKey}] = "
            + $"{failure?.Data[AbstractConnector.CommandTextDataKey]}");
        _out.WriteLine($"  stack head: {failure?.StackTrace?.Split('\n')[0]?.Trim()}");

        var sqlite = failure.Should().BeOfType<SqliteException>(
            "a constraint violation is the caller's own problem and it must arrive as the provider's own "
            + "exception, not as a bare Exception whose message is the SQL").Which;
        sqlite.SqliteErrorCode.Should().Be(19, "SQLITE_CONSTRAINT");

        failure!.Data[AbstractConnector.CommandTextDataKey].Should().NotBeNull();
        Convert.ToString(failure.Data[AbstractConnector.CommandTextDataKey])
            .Should().Contain("INSERT INTO", "the statement is still on the record, just not in the type");

        // Witnesses ExceptionDispatchInfo.Capture(...).Throw() rather than `throw ex`: the original throw
        // site survives. `throw ex` would reset the trace to EnsureSchemaAndReport and the frame below
        // would be gone. (This is the only assertion in this suite that can see it — the funnel probe
        // hands over constructed exceptions, which have no stack.)
        failure.StackTrace.Should().NotBeNullOrEmpty();
        failure.StackTrace.Should().Contain("Microsoft.Data.Sqlite",
            "the trace must still point at where the statement actually failed");
    }
}
