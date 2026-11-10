using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-305 — the default retry policy on PostgreSQL's deadlock (40P01): PostgreSQL aborts the transaction, so it
/// applied nothing and the framework retries it.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL has no deadlock priority, and a waiting session checks for a deadlock <b>once</b>, when its
/// <c>deadlock_timeout</c> (1 s) runs out — it never checks again. So the session that is to be the victim must start
/// waiting when the cycle is already closed (or closes within that second). The arrangement makes that hold by
/// construction, with no timing in it: the framework's statement locks row 1, then waits <i>in SQL</i> until it blocks
/// another session (<see cref="FrameworkStatement"/>), and only then wants row 2 — its own request closes the cycle, so
/// its single check, 1 s later, finds it. The other session holds row 2, waits until the framework holds row 1 and
/// wants row 1 (with a 10 s <c>deadlock_timeout</c>, so it never gives up first), then commits once the framework is
/// aborted; the retry applies.
/// </para>
/// <para>
/// TASK-893: the arrangement used to be "the framework sleeps 300 ms and wants row 2; the other session, once it sees
/// the framework blocked, wants row 1". The framework's one check then ran 1 s after it started waiting, whether or not
/// the cycle had closed. Measured: the poll's own connection took ~2 s to open (unpooled, <c>localhost</c>), while the
/// framework's connector reused a pooled connection from the previous test and was blocked 300 ms in; the cycle closed
/// 1.75 s after the framework started waiting, its check had already found nothing, and the other session's 10 s check
/// aborted the <i>other</i> session — the framework committed and the retry-off test saw nothing thrown (rows (1, 1)).
/// </para>
/// <para>Gated on <c>BIRKO_PG_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</para>
/// </remarks>
public class DeadlockRetryLiveTests : IDisposable
{
    private const string Table = "pg305deadlock";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public DeadlockRetryLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    private static NpgsqlConnection Open()
    {
        var c = new NpgsqlConnection(Settings().GetConnectionString() + ";Pooling=false");
        c.Open();
        return c;
    }

    private static void Exec(NpgsqlConnection c, string sql, NpgsqlTransaction? t = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void ResetTable()
    {
        using var c = Open();
        Exec(c, $"DROP TABLE IF EXISTS {Table}; CREATE TABLE {Table} (id INT PRIMARY KEY, v INT NOT NULL); "
              + $"INSERT INTO {Table} VALUES (1, 0), (2, 0);");
    }

    private static (int, int) ReadRows()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT (SELECT v FROM {Table} WHERE id = 1), (SELECT v FROM {Table} WHERE id = 2)";
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt32(0), r.GetInt32(1));
    }

    /// <summary>
    /// Locks row 1; waits until it blocks another session (pg_locks and pg_blocking_pids are read live, unlike
    /// pg_stat_activity, which is snapshotted per transaction) or until row 2 is free; then wants row 2.
    /// </summary>
    /// <remarks>
    /// On the first attempt the other session is the one blocked, so the request for row 2 closes the cycle. On the
    /// retry nobody is blocked by the framework; the loop ends once the other session has committed and row 2 is free
    /// (<c>FOR UPDATE SKIP LOCKED</c> takes it then, which the next UPDATE does anyway). Bounded at 15 s, so a failed
    /// arrangement fails loudly with its own message instead of hanging.
    /// </remarks>
    private const string FrameworkStatement =
        "UPDATE " + Table + " SET v = v + 1 WHERE id = 1; "
        + "DO $$ DECLARE deadline timestamptz := clock_timestamp() + interval '15 seconds'; BEGIN "
        + "LOOP "
        + "EXIT WHEN EXISTS (SELECT 1 FROM pg_locks l WHERE NOT l.granted AND pg_backend_pid() = ANY (pg_blocking_pids(l.pid))); "
        + "PERFORM 1 FROM " + Table + " WHERE id = 2 FOR UPDATE SKIP LOCKED; "
        + "EXIT WHEN FOUND; "
        + "IF clock_timestamp() > deadline THEN RAISE EXCEPTION 'TASK-893 arrangement: no session ever blocked on row 1'; END IF; "
        + "PERFORM pg_sleep(0.02); "
        + "END LOOP; END $$; "
        + "UPDATE " + Table + " SET v = v + 1 WHERE id = 2;";

    /// <summary>Polls, on its own connection, until the framework's statement holds row 1 (it is in its wait loop).</summary>
    private static void WaitUntilTheFrameworkHoldsRow1()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM pg_stat_activity WHERE wait_event = 'PgSleep' "
                        + $"AND query LIKE '%{Table}%' AND pid <> pg_backend_pid()";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The framework never took row 1: the deadlock could not be arranged.");
            Thread.Sleep(20);
        }
    }

    /// <summary>Holds row 2, then (once the framework holds row 1) wants row 1, then commits.</summary>
    private static Task RunTheOtherSession(ManualResetEventSlim holdingRow2)
        => Task.Run(() =>
        {
            using var c = Open();
            using var t = c.BeginTransaction();
            Exec(c, "SET LOCAL deadlock_timeout = '10s';", t); // never the one to give up
            Exec(c, $"UPDATE {Table} SET v = v + 100 WHERE id = 2;", t);
            holdingRow2.Set();
            // Blocks here until the framework, which waits for exactly this, wants row 2 and is aborted as the victim.
            WaitUntilTheFrameworkHoldsRow1();
            Exec(c, $"UPDATE {Table} SET v = v + 100 WHERE id = 1;", t);
            t.Commit();
        });

    [Fact]
    public async Task A_deadlock_is_retried_by_default_and_applied_once()
    {
        if (!RequireServer()) return;
        ResetTable();
        var connector = new PostgreSQLConnector(Settings());
        connector.RetryPolicy.MaxRetries.Should().Be(2, "the shipped default");
        var attempts = 0;
        connector.OnExecute += _ => Interlocked.Increment(ref attempts);

        using var holding = new ManualResetEventSlim();
        var other = RunTheOtherSession(holding);
        holding.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

        connector.DoCommandWithTransaction(cmd => cmd.CommandText = FrameworkStatement, cmd => cmd.ExecuteNonQuery());
        await other;

        attempts.Should().Be(2, "the first attempt was aborted as the deadlock, the retry applied it");
        ReadRows().Should().Be((101, 101));
    }

    [Fact]
    public async Task With_retry_off_the_deadlock_reaches_the_caller_and_nothing_is_applied()
    {
        if (!RequireServer()) return;
        ResetTable();
        var connector = new PostgreSQLConnector(Settings()) { RetryPolicy = RetryPolicy.None };

        using var holding = new ManualResetEventSlim();
        var other = RunTheOtherSession(holding);
        holding.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

        Action act = () => connector.DoCommandWithTransaction(cmd => cmd.CommandText = FrameworkStatement, cmd => cmd.ExecuteNonQuery());
        var thrown = act.Should().Throw<PostgresException>().Which;
        await other;

        thrown.SqlState.Should().Be("40P01");
        connector.IsNotAppliedException(thrown).Should().BeTrue();
        ReadRows().Should().Be((100, 100));
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { using var c = Open(); Exec(c, $"DROP TABLE IF EXISTS {Table};"); }
        catch { /* best effort */ }
    }
}
