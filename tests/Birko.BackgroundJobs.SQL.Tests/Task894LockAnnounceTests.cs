using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.BackgroundJobs.SQL;
using Birko.Configuration;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.SQL.Tests;

/// <summary>
/// TASK-894 — the advisory-lock statements <see cref="SqlJobLockProvider{DB}"/> sends on its own connection reach the
/// connector's <c>OnExecute</c>, with their values. Measured before on PostgreSQL 16, MySQL 8.4 and SQL Server 2022:
/// the acquire (with and without a wait) and the release announced nothing.
/// </summary>
public abstract class Task894LockAnnounceTests<DB> where DB : AbstractConnector
{
    private readonly ITestOutputHelper _output;
    protected Task894LockAnnounceTests(ITestOutputHelper output) => _output = output;

    protected abstract string Provider { get; }
    protected abstract string HostVariable { get; }
    protected abstract PasswordSettings Settings();

    /// <summary>What the try-acquire, the waiting acquire and the release each send, by a fragment of their text.</summary>
    protected abstract string[] Expected(string lockName);

    private bool Server
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException($"{HostVariable} is not set but BIRKO_REQUIRE_LIVE is.");
            _output.WriteLine($"SKIPPED: {HostVariable} not set.");
            return false;
        }
    }

    [Fact]
    public async Task Acquire_and_release_reach_OnExecute()
    {
        if (!Server) return;
        var name = "t894-" + Guid.NewGuid().ToString("N")[..12];
        var settings = Settings();
        var connector = DataBase.GetConnector<DB>(settings);
        var received = new List<string>();
        OnExecute record = text => { lock (received) received.Add(text); };
        connector.OnExecute += record;
        try
        {
            await using var provider = new SqlJobLockProvider<DB>(settings);
            (await provider.TryAcquireAsync(name, TimeSpan.Zero)).Should().BeTrue();
            await provider.ReleaseAsync(name);
            (await provider.TryAcquireAsync(name, TimeSpan.FromSeconds(1))).Should().BeTrue();
            await provider.ReleaseAsync(name);
        }
        finally { connector.OnExecute -= record; }

        var missing = Expected(name).Where(e => !received.Any(t => t.Contains(e, StringComparison.OrdinalIgnoreCase))).ToList();
        _output.WriteLine($"MEASURE {Provider} | lock acquire/release | announced {received.Count} | missing {missing.Count}: {string.Join(", ", missing)}");
        foreach (var text in received) _output.WriteLine("  " + text);
        missing.Should().BeEmpty();
    }
}

public sealed class PostgreSqlTask894LockAnnounceTests(ITestOutputHelper output) : Task894LockAnnounceTests<PostgreSQLConnector>(output)
{
    protected override string Provider => "PostgreSQL";
    protected override string HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        LiveDatabase.Name("PG"),
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override string[] Expected(string lockName) => new[] { "pg_try_advisory_lock(", "SELECT pg_advisory_lock(", "pg_advisory_unlock(" };
}

public sealed class MySqlTask894LockAnnounceTests(ITestOutputHelper output) : Task894LockAnnounceTests<MySQLConnector>(output)
{
    protected override string Provider => "MySQL";
    protected override string HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        LiveDatabase.Name("MYSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306);
    protected override string[] Expected(string lockName) => new[] { "GET_LOCK('" + lockName, "RELEASE_LOCK('" + lockName };
}

public sealed class MSSqlTask894LockAnnounceTests(ITestOutputHelper output) : Task894LockAnnounceTests<MSSqlConnector>(output)
{
    protected override string Provider => "SQL Server";
    protected override string HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        LiveDatabase.Name("MSSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override string[] Expected(string lockName) => new[] { "sp_getapplock @Resource = '" + lockName, "sp_releaseapplock @Resource = '" + lockName };
}
