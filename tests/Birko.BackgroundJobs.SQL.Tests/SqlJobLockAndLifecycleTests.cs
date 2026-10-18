using System.Linq;
using System;
using System.IO;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.SQL;
using Birko.BackgroundJobs.SQL.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.SQL.Tests;

/// <summary>
/// Covers SqlJobLockProvider (CR-M026 connection leak, CR-M027 false-success on non-Postgres) and the
/// SqlJobQueue FailAsync retry-vs-dead boundary (CR-M028 test-gap). Backed by an offline SQLite DB.
/// </summary>
public class SqlJobLockAndLifecycleTests : IDisposable
{
    private readonly string _dir;
    private readonly SqLiteSettings _settings;

    public SqlJobLockAndLifecycleTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"birko-bjsqllock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _settings = new SqLiteSettings(_dir, "jobs.db");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private SqlJobQueue<SqLiteConnector> NewQueue(RetryPolicy? retry = null)
    {
        var store = new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>();
        store.SetSettings(_settings);
        return new SqlJobQueue<SqLiteConnector>(store, retry);
    }

    [Fact]
    public async Task LockProvider_OnNonPostgres_ReturnsFalse_NotFalseSuccess()
    {
        // CR-M027: SQLite has no cross-connection advisory lock. Previously the DbException path
        // returned true (claiming a lock that was never taken); it must now report failure.
        await using var provider = new SqlJobLockProvider<SqLiteConnector>(_settings);

        var acquired = await provider.TryAcquireAsync("jobs", TimeSpan.FromSeconds(1));

        acquired.Should().BeFalse();
        provider.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task LockProvider_Release_WhenNotHeld_DoesNotThrow()
    {
        await using var provider = new SqlJobLockProvider<SqLiteConnector>(_settings);
        await provider.TryAcquireAsync("jobs", TimeSpan.FromSeconds(1));

        var act = async () => await provider.ReleaseAsync("jobs");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void LockProvider_Dispose_DoesNotThrow()
    {
        var provider = new SqlJobLockProvider<SqLiteConnector>(_settings);
        var act = () => provider.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task FailAsync_WithRetriesRemaining_Reschedules()
    {
        var queue = NewQueue(new RetryPolicy { MaxRetries = 2, BaseDelay = TimeSpan.FromMinutes(1) });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 2 });

        await queue.DequeueAsync(); // AttemptCount -> 1 (< 2)
        await queue.FailAsync(id, "transient");

        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Scheduled);
        job.ScheduledAt.Should().NotBeNull();
        job.LastError.Should().Be("transient");
    }

    [Fact]
    public async Task FailAsync_WhenRetriesExhausted_SetsDead()
    {
        var queue = NewQueue(new RetryPolicy { MaxRetries = 1, BaseDelay = TimeSpan.FromMinutes(1) });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 1 });

        await queue.DequeueAsync(); // AttemptCount -> 1 (not < 1)
        await queue.FailAsync(id, "fatal");

        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Dead);
        job.CompletedAt.Should().NotBeNull();
    }

    // ── TASK-662: MaxRetries null = the policy (2 here), 0 = no retry, n = n attempts ──

    private static readonly RetryPolicy Task662Policy = new() { MaxRetries = 2, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false };

    [Theory]
    [InlineData(null, 2)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    public async Task TASK_662_a_jobs_attempt_budget_is_its_own_or_else_the_policys(int? maxRetries, int attempts)
    {
        var queue = NewQueue(Task662Policy);
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = maxRetries });
        (await queue.GetAsync(id))!.MaxRetries.Should().Be(maxRetries, "null is stored as null, not as a default");

        var claims = 0;
        for (var i = 0; i < 40 && claims < 10; i++)
        {
            var job = await queue.DequeueAsync();
            if (job == null)
            {
                if ((await queue.GetAsync(id))!.Status == JobStatus.Dead) break;
                await Task.Delay(25);
                continue;
            }
            claims++;
            await queue.FailAsync(job.Guid, "boom");
        }

        claims.Should().Be(attempts);
        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Dead);
    }

    // TASK-662: the column the change needs, measured. A table created before it has MaxRetries NOT NULL, refuses a
    // null budget, and DetectDrift says so; a table created now is clean.
    [Fact]
    public async Task TASK_662_a_table_created_before_the_change_reports_MaxRetries_as_nullability_drift()
    {
        await NewQueue().EnqueueAsync(new JobDescriptor { JobType = "fresh", MaxRetries = null });
        var connector = new SqLiteConnector(_settings);
        connector.DetectDrift(typeof(JobDescriptorModel)).Drifts.Should().BeEmpty("a table created now accepts null");

        using (var raw = new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()))
        {
            raw.Open();
            using var read = raw.CreateCommand();
            read.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '__BackgroundJobs'";
            var create = (string)read.ExecuteScalar()!;
            var old = System.Text.RegularExpressions.Regex.Replace(create, @"\bMaxRetries\s+(\w+)", "MaxRetries $1 NOT NULL");
            old.Should().NotBe(create);
            using var rebuild = raw.CreateCommand();
            rebuild.CommandText = "DROP TABLE \"__BackgroundJobs\"; " + old;
            rebuild.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        connector.DetectDrift(typeof(JobDescriptorModel)).Drifts
            .Select(d => (d.Column, Kind: d.Kind.ToString()))
            .Should().BeEquivalentTo(new[] { ("MaxRetries", "NullabilityMismatch") });
        var refused = () => NewQueue().EnqueueAsync(new JobDescriptor { JobType = "old", MaxRetries = null });
        await refused.Should().ThrowAsync<Exception>("the old column refuses a null budget");
        await NewQueue().EnqueueAsync(new JobDescriptor { JobType = "old", MaxRetries = 3 });
    }

    // ── TASK-664: a job a dead worker left in Processing is reclaimed after the claim timeout ──

    private static readonly TimeSpan Task664Timeout = TimeSpan.FromMilliseconds(300);

    private SqlJobQueue<SqLiteConnector> Task664Queue(RetryPolicy policy)
    {
        var store = new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>();
        store.SetSettings(_settings);
        return new SqlJobQueue<SqLiteConnector>(store, policy, Task664Timeout);
    }

    [Fact]
    public async Task TASK_664_an_abandoned_job_runs_again_after_the_claim_timeout()
    {
        var queue = Task664Queue(new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        (await queue.DequeueAsync())!.Guid.Should().Be(id, "claimed by a worker that then dies");
        (await queue.DequeueAsync()).Should().BeNull("a job claimed just now is not reclaimed");
        await Task.Delay(Task664Timeout * 2);
        var again = await queue.DequeueAsync();
        again.Should().NotBeNull("a job held in Processing past the claim timeout is offered again");
        again!.Guid.Should().Be(id);
        again.AttemptCount.Should().Be(2);
        again.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task TASK_664_an_abandoned_job_with_no_attempts_left_is_buried()
    {
        var queue = Task664Queue(new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 1 });
        (await queue.DequeueAsync())!.Guid.Should().Be(id);
        await Task.Delay(Task664Timeout * 2);
        (await queue.DequeueAsync()).Should().BeNull("its one attempt is spent");
        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Dead);
        job.LastError.Should().StartWith("Abandoned: held in Processing");
        job.CompletedAt.Should().NotBeNull();
    }
}
