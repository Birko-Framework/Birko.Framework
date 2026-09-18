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
}
