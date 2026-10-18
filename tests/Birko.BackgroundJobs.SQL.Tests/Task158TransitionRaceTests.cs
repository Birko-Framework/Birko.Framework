using System;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.SQL;
using Birko.BackgroundJobs.SQL.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.SQL.Tests;

/// <summary>
/// TASK-158 — SH-M012 (the claim guard was Status-only, so a stale worker re-claimed a job another worker had run and
/// rescheduled, ignoring its backoff and rewriting AttemptCount downward) and SH-M013 (Complete / Fail / Cancel wrote the
/// whole row from a stale snapshot, clobbering a newer claim). Each race is forced: a store hook runs the other worker's
/// whole step just before this worker's write.
/// </summary>
public class Task158TransitionRaceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"birko-bj158-{Guid.NewGuid():N}");
    private readonly SqLiteSettings _settings;

    public Task158TransitionRaceTests()
    {
        Directory.CreateDirectory(_dir);
        _settings = new SqLiteSettings(_dir, "jobs.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class Interleaving : AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>
    {
        public Func<Task>? BeforeFirstWrite { get; set; }

        private async Task FireOnce()
        {
            var hook = BeforeFirstWrite;
            BeforeFirstWrite = null;
            if (hook != null) await hook();
        }

        public override async Task UpdateAsync(Expression<Func<JobDescriptorModel, bool>> filter, PropertyUpdate<JobDescriptorModel> updates, CancellationToken ct = default)
        {
            await FireOnce();
            await base.UpdateAsync(filter, updates, ct);
        }

        public override async Task UpdateAsync(JobDescriptorModel data, StoreDataDelegate<JobDescriptorModel>? processDelegate = null, CancellationToken ct = default)
        {
            await FireOnce();
            await base.UpdateAsync(data, processDelegate, ct);
        }
    }

    private SqlJobQueue<SqLiteConnector> Queue(AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>? store = null)
    {
        store ??= new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>();
        store.SetSettings(_settings);
        return new SqlJobQueue<SqLiteConnector>(store, claimTimeout: TimeSpan.FromMinutes(5));
    }

    private async Task<JobDescriptorModel> Row(Guid id)
    {
        var store = new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>();
        store.SetSettings(_settings);
        return (await store.ReadFirstAsync(j => j.Guid == id))!;
    }

    [Fact]
    public async Task SH_M012_a_stale_claim_does_not_take_a_job_another_worker_ran_and_rescheduled()
    {
        var other = Queue();
        var id = await other.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 5 });
        // A retry that is due: Scheduled, one attempt behind it, ScheduledAt in the past.
        await other.DequeueAsync();
        await other.FailAsync(id, "attempt 1");
        var due = await Row(id);
        due.ScheduledAt = DateTime.UtcNow.AddMinutes(-1);
        var raw = new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>();
        raw.SetSettings(_settings);
        await raw.UpdateAsync(due);

        // Worker B has read that Scheduled candidate; before its claim lands, worker A claims, runs and fails it, so it
        // is Scheduled again — with a future backoff and two attempts behind it.
        var slow = new Interleaving
        {
            BeforeFirstWrite = async () =>
            {
                (await other.DequeueAsync())!.Guid.Should().Be(id);
                await other.FailAsync(id, "first attempt failed");
            },
        };
        var claimed = await Queue(slow).DequeueAsync();

        claimed.Should().BeNull("the job B read is no longer the job in the store");
        var row = await Row(id);
        row.Status.Should().Be((int)JobStatus.Scheduled);
        row.AttemptCount.Should().Be(2);
        row.ScheduledAt.Should().BeAfter(DateTime.UtcNow, "the backoff stands");
    }

    [Fact]
    public async Task SH_M013_completing_does_not_overwrite_a_newer_state_of_the_job()
    {
        var queue = Queue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 5 });
        await queue.DequeueAsync();

        // The completing worker reads the row; before its write lands, the job is failed and rescheduled by a duplicate.
        var slow = new Interleaving { BeforeFirstWrite = () => queue.FailAsync(id, "duplicate failed") };
        await Queue(slow).CompleteAsync(id);

        var row = await Row(id);
        row.Status.Should().Be((int)JobStatus.Scheduled, "only a Processing job is completed");
        row.LastError.Should().Be("duplicate failed");
    }

    [Fact]
    public async Task SH_M013_cancelling_does_not_overwrite_a_job_a_worker_just_claimed()
    {
        var queue = Queue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });

        var slow = new Interleaving { BeforeFirstWrite = async () => (await queue.DequeueAsync())!.Guid.Should().Be(id) };
        var cancelled = await Queue(slow).CancelAsync(id);

        cancelled.Should().BeFalse("the job was Processing by the time the cancel was written");
        var row = await Row(id);
        row.Status.Should().Be((int)JobStatus.Processing);
        row.AttemptCount.Should().Be(1);
        row.ClaimToken.Should().NotBeNull();
    }

    [Fact]
    public async Task SH_M013_failing_writes_only_its_fields_and_keeps_a_newer_claim()
    {
        var queue = Queue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 5 });
        await queue.DequeueAsync();

        // The failing worker read attempt 1; meanwhile the job is failed and claimed again (attempt 2).
        var slow = new Interleaving
        {
            BeforeFirstWrite = async () =>
            {
                await queue.FailAsync(id, "attempt 1");
                var row = await Row(id);
                row.ScheduledAt = DateTime.UtcNow.AddMinutes(-1); // due now
                var store = new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>();
                store.SetSettings(_settings);
                await store.UpdateAsync(row);
                (await queue.DequeueAsync())!.Guid.Should().Be(id);
            },
        };
        await Queue(slow).FailAsync(id, "stale report");

        var after = await Row(id);
        after.Status.Should().Be((int)JobStatus.Processing, "the newer claim stands");
        after.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task The_ordinary_transitions_still_work()
    {
        var queue = Queue();
        var a = await queue.EnqueueAsync(new JobDescriptor { JobType = "a", MaxRetries = 1 });
        var b = await queue.EnqueueAsync(new JobDescriptor { JobType = "b" });

        await queue.DequeueAsync();
        await queue.CompleteAsync(a);
        (await queue.CancelAsync(b)).Should().BeTrue();
        (await queue.CancelAsync(b)).Should().BeFalse();

        (await Row(a)).Status.Should().Be((int)JobStatus.Completed);
        (await Row(b)).Status.Should().Be((int)JobStatus.Cancelled);
    }
}
