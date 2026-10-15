using System;
using System.IO;
using System.Linq;
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
/// TASK-575. A reclaim of a stale Processing job claimed with <c>WHERE Status = Processing</c>, the very status the claim
/// writes, so a second worker's identical UPDATE still matched after the first one's. And the Dead burial was an
/// unguarded read-modify-write. Both races are forced deterministically: a store hook runs the other party's whole step
/// just before this worker's write, which is the interleaving two real workers can hit.
/// </summary>
public class StaleReclaimRaceTests : IDisposable
{
    private readonly string _dir;
    private readonly SqLiteSettings _settings;

    public StaleReclaimRaceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"birko-bjrace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _settings = new SqLiteSettings(_dir, "jobs.db");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>Runs <see cref="BeforeFirstWrite"/> once, just before this store's first update of any kind.</summary>
    private sealed class InterleavingStore : AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>
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

    private SqlJobQueue<SqLiteConnector> Queue(AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel> store)
    {
        store.SetSettings(_settings);
        return new SqlJobQueue<SqLiteConnector>(store, claimTimeout: TimeSpan.FromMinutes(5));
    }

    /// <summary>A job left in Processing by a worker that stopped an hour ago.</summary>
    private async Task<Guid> StaleProcessingJob(int attemptCount, int maxRetries)
    {
        var store = new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>();
        var queue = Queue(store);
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = maxRetries });
        var model = (await store.ReadFirstAsync(j => j.Guid == id))!;
        model.Status = (int)JobStatus.Processing;
        model.AttemptCount = attemptCount;
        model.LastAttemptAt = DateTime.UtcNow.AddHours(-1);
        model.ClaimToken = Guid.NewGuid();
        await store.UpdateAsync(model);
        return id;
    }

    [Fact]
    public async Task Two_workers_reclaiming_one_stale_job_hand_it_to_exactly_one()
    {
        var id = await StaleProcessingJob(attemptCount: 1, maxRetries: 3);

        var storeA = new InterleavingStore();
        var workerA = Queue(storeA);
        var workerB = Queue(new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>());
        JobDescriptor? gotB = null;
        storeA.BeforeFirstWrite = async () => gotB = await workerB.DequeueAsync();

        var gotA = await workerA.DequeueAsync();

        new[] { gotA, gotB }.Count(j => j?.Guid == id).Should().Be(1, "a stale job is reclaimed by one worker, never run twice");
    }

    [Fact]
    public async Task A_burial_never_overwrites_a_job_its_slow_worker_completed_meanwhile()
    {
        var id = await StaleProcessingJob(attemptCount: 3, maxRetries: 3);

        var storeA = new InterleavingStore();
        var workerA = Queue(storeA);
        var slowWorker = Queue(new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>());
        storeA.BeforeFirstWrite = () => slowWorker.CompleteAsync(id);

        await workerA.DequeueAsync();

        var after = new AsyncDataBaseBulkStore<SqLiteConnector, JobDescriptorModel>();
        after.SetSettings(_settings);
        (await after.ReadFirstAsync(j => j.Guid == id))!.Status.Should().Be((int)JobStatus.Completed,
            "the worker was slow, not dead: its completion must not be buried");
    }
}
