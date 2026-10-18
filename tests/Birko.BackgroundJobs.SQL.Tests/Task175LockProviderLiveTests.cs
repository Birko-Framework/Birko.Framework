using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Birko.BackgroundJobs.SQL;
using Birko.Configuration;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.SQL.Tests;

/// <summary>A connector deriving from PostgreSQL's whose own name says nothing about PostgreSQL (SH-L022).</summary>
public class TenantDbConnector : PostgreSQLConnector
{
    public TenantDbConnector(RemoteSettings settings) : base(settings) { }
}

/// <summary>
/// TASK-175 on a live PostgreSQL: the acquire timeout is honoured (SH-L020: <c>pg_try_advisory_lock</c> returned at
/// once) and a connector deriving from PostgreSQL's locks (SH-L022: the dialect was read from the type's own name, so
/// it resolved to "unsupported" and never locked). Gated on <c>BIRKO_PG_HOST</c>.
/// </summary>
public sealed class Task175LockProviderLiveTests(ITestOutputHelper output)
{
    private PasswordSettings? Settings()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_PG_HOST is not set but BIRKO_REQUIRE_LIVE is.");
            output.WriteLine("SKIPPED: BIRKO_PG_HOST not set.");
            return null;
        }
        return new PostgreSqlSettings(host,
            Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
            Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
            Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
        { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    }

    private static string Name() => "t175-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task SH_L020_an_acquire_waits_for_a_holder_that_releases_within_the_timeout()
    {
        if (Settings() is not { } settings) return;
        var name = Name();
        await using var holder = new SqlJobLockProvider<PostgreSQLConnector>(settings);
        await using var waiter = new SqlJobLockProvider<PostgreSQLConnector>(settings);
        (await holder.TryAcquireAsync(name, TimeSpan.Zero)).Should().BeTrue();

        var watch = Stopwatch.StartNew();
        var acquire = waiter.TryAcquireAsync(name, TimeSpan.FromSeconds(5));
        await Task.Delay(500);
        await holder.ReleaseAsync(name);

        (await acquire).Should().BeTrue("the holder let go within the timeout");
        watch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(400), "it waited rather than answering at once");
    }

    [Fact]
    public async Task SH_L020_an_acquire_against_a_holder_that_keeps_it_fails_after_the_timeout()
    {
        if (Settings() is not { } settings) return;
        var name = Name();
        await using var holder = new SqlJobLockProvider<PostgreSQLConnector>(settings);
        await using var waiter = new SqlJobLockProvider<PostgreSQLConnector>(settings);
        (await holder.TryAcquireAsync(name, TimeSpan.Zero)).Should().BeTrue();

        var watch = Stopwatch.StartNew();
        (await waiter.TryAcquireAsync(name, TimeSpan.FromMilliseconds(600))).Should().BeFalse();
        watch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(500));
        waiter.IsLocked.Should().BeFalse();

        // The waiter's failed attempt left nothing behind: once released, it can take the lock.
        await holder.ReleaseAsync(name);
        (await waiter.TryAcquireAsync(name, TimeSpan.FromSeconds(1))).Should().BeTrue();
    }

    [Fact]
    public async Task SH_L022_a_connector_deriving_from_PostgreSQLs_locks()
    {
        if (Settings() is not { } settings) return;
        var name = Name();
        await using var derived = new SqlJobLockProvider<TenantDbConnector>(settings);
        await using var plain = new SqlJobLockProvider<PostgreSQLConnector>(settings);

        (await derived.TryAcquireAsync(name, TimeSpan.Zero)).Should().BeTrue("it is a PostgreSQL connector");
        (await plain.TryAcquireAsync(name, TimeSpan.Zero)).Should().BeFalse("and it holds the same advisory lock");
    }
}
