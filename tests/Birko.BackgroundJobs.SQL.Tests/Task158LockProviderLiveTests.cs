using System;
using System.Threading.Tasks;
using Birko.BackgroundJobs.SQL;
using Birko.Configuration;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.SQL.Tests;

/// <summary>
/// TASK-158 — <see cref="SqlJobLockProvider{DB}"/> on each live server: SH-M006 (a provider holding one lock answered
/// "true" for another name, and released by whatever name it was given) and SH-M019 (Dispose closed the connection
/// without unlocking, and with pooling the session — and its lock — outlived the provider). Gated on the server's
/// <c>BIRKO_*_HOST</c>; reported as skipped without one, and failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public abstract class Task158LockProviderLiveTests<DB> where DB : AbstractConnector
{
    private readonly ITestOutputHelper _output;
    protected Task158LockProviderLiveTests(ITestOutputHelper output) => _output = output;

    protected abstract string HostVariable { get; }
    protected abstract PasswordSettings Settings();

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

    private static string Name() => "t158-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task SH_M006_a_provider_holding_one_lock_refuses_to_claim_another_name()
    {
        if (!Server) return;
        var a = Name();
        var b = Name();
        await using var provider = new SqlJobLockProvider<DB>(Settings());

        (await provider.TryAcquireAsync(a, TimeSpan.Zero)).Should().BeTrue();
        (await provider.TryAcquireAsync(a, TimeSpan.Zero)).Should().BeTrue("re-acquiring the held name is idempotent");

        var other = async () => await provider.TryAcquireAsync(b, TimeSpan.Zero);
        await other.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{a}*");

        // b was never taken, so another provider can have it.
        await using var second = new SqlJobLockProvider<DB>(Settings());
        (await second.TryAcquireAsync(b, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public async Task SH_M006_releasing_a_name_the_provider_does_not_hold_keeps_the_lock_it_holds()
    {
        if (!Server) return;
        var a = Name();
        await using var provider = new SqlJobLockProvider<DB>(Settings());
        await provider.TryAcquireAsync(a, TimeSpan.Zero);

        await provider.ReleaseAsync(Name());

        provider.IsLocked.Should().BeTrue();
        await using var second = new SqlJobLockProvider<DB>(Settings());
        (await second.TryAcquireAsync(a, TimeSpan.Zero)).Should().BeFalse("a is still held");
    }

    /// <summary>Settings that open a different connection pool (another database) on the same server.</summary>
    protected virtual PasswordSettings? OtherPoolSettings() => null;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SH_M019_disposing_frees_the_lock_for_a_provider_on_another_pool(bool async)
    {
        if (!Server || OtherPoolSettings() is not { } other) return;
        var a = Name();
        var provider = new SqlJobLockProvider<DB>(Settings());
        (await provider.TryAcquireAsync(a, TimeSpan.Zero)).Should().BeTrue();

        if (async) await provider.DisposeAsync();
        else provider.Dispose();

        await using var elsewhere = new SqlJobLockProvider<DB>(other);
        (await elsewhere.TryAcquireAsync(a, TimeSpan.Zero)).Should().BeTrue("an idle pooled session must not keep the lock");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SH_M019_disposing_a_provider_frees_its_lock_for_the_next_one(bool async)
    {
        if (!Server) return;
        var a = Name();
        var provider = new SqlJobLockProvider<DB>(Settings());
        (await provider.TryAcquireAsync(a, TimeSpan.Zero)).Should().BeTrue();

        if (async) await provider.DisposeAsync();
        else provider.Dispose();

        await using var next = new SqlJobLockProvider<DB>(Settings());
        (await next.TryAcquireAsync(a, TimeSpan.Zero)).Should().BeTrue("the lock must not outlive the provider in the connection pool");
    }
}

public sealed class PostgreSqlTask158LockProviderLiveTests(ITestOutputHelper output) : Task158LockProviderLiveTests<PostgreSQLConnector>(output)
{
    protected override string HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
}

public sealed class MySqlTask158LockProviderLiveTests(ITestOutputHelper output) : Task158LockProviderLiveTests<MySQLConnector>(output)
{
    protected override string HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306);

    // GET_LOCK is server-wide, so a provider on another database (another pool) contends for the same lock.
    protected override PasswordSettings? OtherPoolSettings() => new MySqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!, "mysql",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var q) ? q : 3306);
}

public sealed class MSSqlTask158LockProviderLiveTests(ITestOutputHelper output) : Task158LockProviderLiveTests<MSSqlConnector>(output)
{
    protected override string HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
}
