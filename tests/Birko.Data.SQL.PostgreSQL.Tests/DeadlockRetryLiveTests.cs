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
/// PostgreSQL has no deadlock priority: the session whose <c>deadlock_timeout</c> (1 s) runs out first finds the cycle
/// and aborts itself. So the framework is made to wait first: it locks row 1, sleeps, then wants row 2, which the other
/// session holds; 300 ms later the other session wants row 1, closing the cycle; at 1 s the framework's check aborts it.
/// The other session then commits and the retry applies. Asserted: the rows (each +1 exactly once) and the attempts.
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

    private const string FrameworkStatement =
        "UPDATE " + Table + " SET v = v + 1 WHERE id = 1; SELECT pg_sleep(0.3); "
        + "UPDATE " + Table + " SET v = v + 1 WHERE id = 2;";

    /// <summary>Holds row 2, then (after the framework is waiting for it) wants row 1, then commits.</summary>
    private static Task RunTheOtherSession(ManualResetEventSlim holdingRow2)
        => Task.Run(() =>
        {
            using var c = Open();
            using var t = c.BeginTransaction();
            Exec(c, "SET LOCAL deadlock_timeout = '10s';", t); // never the one to give up
            Exec(c, $"UPDATE {Table} SET v = v + 100 WHERE id = 2;", t);
            holdingRow2.Set();
            Thread.Sleep(600); // the framework holds row 1 and is waiting for row 2
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
