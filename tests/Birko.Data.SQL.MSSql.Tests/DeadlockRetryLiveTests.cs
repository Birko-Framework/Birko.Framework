using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-305 — the default retry policy, on the error it exists for: a SQL Server deadlock victim (1205), which SQL Server
/// rolls back and answers "Rerun the transaction".
/// </summary>
/// <remarks>
/// <para>
/// A real deadlock, made deterministic: the framework's statement locks row 1, waits, then wants row 2; a second
/// session holds row 2 and then wants row 1, with <c>DEADLOCK_PRIORITY HIGH</c> so the framework is the victim. The
/// second session then commits, and the framework's retry applies its statement. The assertion is the resulting rows
/// (each +1 exactly once), not "did not throw", and the attempt count.
/// </para>
/// <para>
/// With the old <c>RetryPolicy.None</c> the write failed (the third test pins that, as the policy-off case). A suspicion
/// was measured and falsified: that <c>Rollback()</c> on the victim's already-ended transaction would throw and hide the
/// 1205 from the retry. It does not, on Microsoft.Data.SqlClient against 2022 — with the plain <c>Rollback()</c> all
/// three tests pass — so no guard was added for it.
/// </para>
/// <para>Gated on <c>BIRKO_MSSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</para>
/// </remarks>
public class DeadlockRetryLiveTests : IDisposable
{
    private const string Table = "Ms305Deadlock";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => LiveDatabase.Name("MSSQL");
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public DeadlockRetryLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port) { TrustServerCertificate = true };

    private static string RawConnectionString() => new SqlConnectionStringBuilder
    {
        DataSource = $"{Host},{Port}", InitialCatalog = Database, UserID = User, Password = Password,
        TrustServerCertificate = true, Pooling = false,
    }.ConnectionString;

    private static void Exec(SqlConnection c, string sql, SqlTransaction? t = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = t;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void ResetTable()
    {
        using var c = new SqlConnection(RawConnectionString());
        c.Open();
        Exec(c, $"IF OBJECT_ID('dbo.{Table}') IS NOT NULL DROP TABLE dbo.{Table}; "
              + $"CREATE TABLE dbo.{Table} (Id INT PRIMARY KEY, V INT NOT NULL); "
              + $"INSERT INTO dbo.{Table} VALUES (1, 0), (2, 0);");
    }

    private static (int, int) ReadRows()
    {
        using var c = new SqlConnection(RawConnectionString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT (SELECT V FROM dbo.{Table} WHERE Id = 1), (SELECT V FROM dbo.{Table} WHERE Id = 2)";
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt32(0), r.GetInt32(1));
    }

    /// <summary>Locks row 1, waits for the other session to hold row 2, then wants row 2.</summary>
    private const string FrameworkStatement =
        "UPDATE dbo." + Table + " SET V = V + 1 WHERE Id = 1; WAITFOR DELAY '00:00:00.600'; "
        + "UPDATE dbo." + Table + " SET V = V + 1 WHERE Id = 2;";

    /// <summary>Polls, on its own connection, until another session's statement on the table is in its WAITFOR or a lock wait.</summary>
    private static void WaitUntilTheFrameworkHoldsRow1()
    {
        using var c = new SqlConnection(RawConnectionString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) s "
                        + "WHERE r.session_id <> @@SPID AND (r.wait_type = 'WAITFOR' OR r.wait_type LIKE 'LCK[_]M[_]%') "
                        + $"AND s.text LIKE '%{Table}%'";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Convert.ToInt32(cmd.ExecuteScalar()) == 0)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The framework never took row 1: the deadlock could not be arranged.");
            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// The other session: holds row 2, wants row 1 while the framework holds it, wins the deadlock (HIGH priority), then
    /// commits. Started before the framework's statement; its lock on row 2 is taken at once.
    /// </summary>
    private static Task RunTheOtherSession(ManualResetEventSlim holdingRow2)
        => Task.Run(() =>
        {
            using var c = new SqlConnection(RawConnectionString());
            c.Open();
            Exec(c, "SET DEADLOCK_PRIORITY HIGH; SET LOCK_TIMEOUT 10000;");
            using var t = c.BeginTransaction();
            Exec(c, $"UPDATE dbo.{Table} SET V = V + 100 WHERE Id = 2;", t);
            holdingRow2.Set();
            // Wait until the framework holds row 1 (its WAITFOR, or already blocked on row 2), then close the cycle. This
            // was a fixed 300 ms sleep; a slow start left no deadlock (the PostgreSQL twin failed so on CI, da4eeecc).
            WaitUntilTheFrameworkHoldsRow1();
            Exec(c, $"UPDATE dbo.{Table} SET V = V + 100 WHERE Id = 1;", t);
            t.Commit();
        });

    [Fact]
    public async Task A_deadlock_victim_is_retried_by_default_and_applied_once()
    {
        if (!RequireServer()) return;
        ResetTable();
        var connector = new MSSqlConnector(Settings());
        connector.RetryPolicy.MaxRetries.Should().Be(2, "the shipped default, not one set by this test");
        var attempts = 0;
        connector.OnExecute += _ => Interlocked.Increment(ref attempts);

        using var holding = new ManualResetEventSlim();
        var other = RunTheOtherSession(holding);
        holding.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

        connector.DoCommandWithTransaction(cmd => cmd.CommandText = FrameworkStatement, cmd => cmd.ExecuteNonQuery());
        await other;

        attempts.Should().Be(2, "the first attempt was the deadlock victim, the retry applied it");
        ReadRows().Should().Be((101, 101), "each row got the other session's +100 and the framework's +1 exactly once");
    }

    [Fact]
    public async Task Async_a_deadlock_victim_is_retried_by_default_and_applied_once()
    {
        if (!RequireServer()) return;
        ResetTable();
        var connector = new MSSqlConnector(Settings());
        var attempts = 0;
        connector.OnExecute += _ => Interlocked.Increment(ref attempts);

        using var holding = new ManualResetEventSlim();
        var other = RunTheOtherSession(holding);
        holding.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

        await connector.DoCommandWithTransactionAsync(
            cmd => { cmd.CommandText = FrameworkStatement; return Task.CompletedTask; },
            async cmd => await cmd.ExecuteNonQueryAsync());
        await other;

        attempts.Should().Be(2);
        ReadRows().Should().Be((101, 101));
    }

    [Fact]
    public async Task With_retry_off_the_caller_sees_the_deadlock_itself_and_nothing_is_applied()
    {
        if (!RequireServer()) return;
        ResetTable();
        var connector = new MSSqlConnector(Settings()) { RetryPolicy = RetryPolicy.None };

        using var holding = new ManualResetEventSlim();
        var other = RunTheOtherSession(holding);
        holding.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

        Action act = () => connector.DoCommandWithTransaction(cmd => cmd.CommandText = FrameworkStatement, cmd => cmd.ExecuteNonQuery());
        var thrown = act.Should().Throw<SqlException>("the victim's own error reaches the caller").Which;
        await other;

        thrown.Number.Should().Be(1205);
        connector.IsNotAppliedException(thrown).Should().BeTrue();
        ReadRows().Should().Be((100, 100), "the victim's +1 on row 1 was rolled back by SQL Server");
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try
        {
            using var c = new SqlConnection(RawConnectionString());
            c.Open();
            Exec(c, $"IF OBJECT_ID('dbo.{Table}') IS NOT NULL DROP TABLE dbo.{Table};");
        }
        catch { /* best effort */ }
    }
}
