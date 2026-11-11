using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.SQL;
using Birko.BackgroundJobs.SQL.Models;
using Birko.Configuration;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.SQL.Tests;

/// <summary>
/// TASK-900 (TASK-879 on RavenDB, TASK-884 on Elasticsearch): a claim's <c>LastAttemptAt</c> was the time the dequeue
/// BEGAN, taken before the candidate read. A read slower than the claim timeout therefore wrote a claim that was already
/// stale, and the next dequeue handed the same job out again. The store double delays only the candidate read (the
/// ordered, limited one), so the claim and its read-back run at full speed. SQLite offline; PostgreSQL, MySQL and SQL
/// Server live, gated on <c>BIRKO_*_HOST</c> and failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public abstract class Task900ClaimStampTests<DB> where DB : AbstractConnector
{
    protected static readonly TimeSpan ClaimTimeout = TimeSpan.FromMilliseconds(300);

    private readonly ITestOutputHelper _output;
    protected Task900ClaimStampTests(ITestOutputHelper output) => _output = output;

    /// <summary>The variable that gates a live server, or null for an offline provider.</summary>
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();

    /// <summary>The real store, with a candidate read that takes longer than the claim timeout.</summary>
    private sealed class SlowCandidateReadStore : AsyncDataBaseBulkStore<DB, JobDescriptorModel>
    {
        protected override async Task<IEnumerable<JobDescriptorModel>> ReadCoreAsync(
            Expression<Func<JobDescriptorModel, bool>>? filter = null, OrderBy<JobDescriptorModel>? orderBy = null,
            int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            await Task.Delay(ClaimTimeout + TimeSpan.FromMilliseconds(150), ct);
            return await base.ReadCoreAsync(filter, orderBy, limit, offset, ct);
        }
    }

    private bool Available()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException($"{HostVariable} is not set but BIRKO_REQUIRE_LIVE is.");
        _output.WriteLine($"SKIPPED: {HostVariable} not set.");
        return false;
    }

    [Fact]
    public async Task TASK_900_a_claim_behind_a_slow_candidate_read_is_not_reclaimed_at_once()
    {
        if (!Available()) return;
        var store = new SlowCandidateReadStore();
        store.SetSettings(Settings());
        var queue = new SqlJobQueue<DB>(store, new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false }, ClaimTimeout);
        // The job table has a fixed name on a shared server, so the job carries its own queue and is removed afterwards.
        var queueName = "t900-" + Guid.NewGuid().ToString("N")[..12];
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t900", QueueName = queueName, Priority = int.MaxValue });
        try
        {
            var claimed = await queue.DequeueAsync(queueName);
            var afterClaim = DateTime.UtcNow;
            claimed!.Guid.Should().Be(id);
            claimed.LastAttemptAt.Should().BeOnOrAfter(afterClaim - ClaimTimeout, "the claim is stamped when it is written, after the candidate read");
            (await queue.DequeueAsync(queueName)).Should().BeNull("a job claimed just now is not reclaimed, however long the read that found it took");
            (await queue.GetAsync(id))!.AttemptCount.Should().Be(1);
        }
        finally
        {
            var cleanup = new AsyncDataBaseBulkStore<DB, JobDescriptorModel>();
            cleanup.SetSettings(Settings());
            await cleanup.DeleteAsync(j => j.Guid == id);
        }
    }
}

public sealed class SqLiteTask900ClaimStampTests : Task900ClaimStampTests<SqLiteConnector>, IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"birko-bj900-{Guid.NewGuid():N}");

    public SqLiteTask900ClaimStampTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_dir);

    protected override string? HostVariable => null;
    protected override PasswordSettings Settings() => new SqLiteSettings(_dir, "jobs.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TestTempDirectory.Delete(_dir);
    }
}

public sealed class PostgreSqlTask900ClaimStampTests(ITestOutputHelper output) : Task900ClaimStampTests<PostgreSQLConnector>(output)
{
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        LiveDatabase.Name("PG"),
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
}

public sealed class MySqlTask900ClaimStampTests(ITestOutputHelper output) : Task900ClaimStampTests<MySQLConnector>(output)
{
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        LiveDatabase.Name("MYSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306);
}

public sealed class MSSqlTask900ClaimStampTests(ITestOutputHelper output) : Task900ClaimStampTests<MSSqlConnector>(output)
{
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        LiveDatabase.Name("MSSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
}
