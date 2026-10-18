using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.JSON;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.JSON.Tests;

/// <summary>
/// Coverage for the JSON-file job queue (CR-M019): the public lifecycle, the FailAsync retry-vs-dead
/// boundary, PurgeAsync cutoff, and the CR-M018 concurrency guard — the SemaphoreSlim added to
/// DequeueAsync must prevent two in-process workers from claiming the same job. Backed by a temp dir.
/// </summary>
public class JsonJobQueueTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;
    private readonly TestDateTimeProvider _clock;

    public JsonJobQueueTests()
    {
        // Relative location: PathValidator rejects absolute Windows paths (the drive-letter ':').
        _location = $"birko-bjjson-{Guid.NewGuid():N}";
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
        _clock = new TestDateTimeProvider(new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private JsonJobQueue NewQueue(RetryPolicy? retry = null) =>
        new(new Birko.Configuration.Settings(_location, "jobs"), _clock, retry);

    [Fact]
    public async Task Enqueue_Dequeue_Complete_RoundTrips()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });

        var dequeued = await queue.DequeueAsync();
        dequeued!.Guid.Should().Be(id);
        dequeued.Status.Should().Be(JobStatus.Processing);
        dequeued.AttemptCount.Should().Be(1);

        await queue.CompleteAsync(id);
        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task Dequeue_WhenEmpty_ReturnsNull()
    {
        var queue = NewQueue();
        (await queue.DequeueAsync()).Should().BeNull();
    }

    [Fact]
    public async Task FailAsync_WithRetriesRemaining_Reschedules()
    {
        var queue = NewQueue(new RetryPolicy { MaxRetries = 2, BaseDelay = TimeSpan.FromMinutes(1) });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 2 });

        await queue.DequeueAsync(); // AttemptCount -> 1
        await queue.FailAsync(id, "boom");

        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Scheduled);
        job.ScheduledAt.Should().NotBeNull();
        job.LastError.Should().Be("boom");
    }

    [Fact]
    public async Task FailAsync_WhenRetriesExhausted_SetsDead()
    {
        var queue = NewQueue(new RetryPolicy { MaxRetries = 1, BaseDelay = TimeSpan.FromMinutes(1) });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 1 });

        await queue.DequeueAsync(); // AttemptCount -> 1, not < MaxRetries(1)
        await queue.FailAsync(id, "boom");

        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Dead);
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FailAsync_JobMaxRetriesZero_FallsBackToPolicyMaxRetries()
    {
        // Regression for CR-L025: a job with MaxRetries == 0 went straight to Dead on first failure,
        // ignoring the queue's RetryPolicy.MaxRetries. It must now fall back to the policy (mirroring
        // the reference InMemoryJobQueue), so the job reschedules while the policy has retries left.
        var queue = NewQueue(new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.FromMinutes(1) });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 0 });

        await queue.DequeueAsync(); // AttemptCount -> 1, below the policy's 3
        await queue.FailAsync(id, "boom");

        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Scheduled);
        job.ScheduledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PurgeAsync_RemovesTerminalJobsOlderThanCutoff()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        await queue.DequeueAsync();
        await queue.CompleteAsync(id);

        // Advance the clock so the completed job is older than the cutoff.
        _clock.Advance(TimeSpan.FromHours(2));
        var purged = await queue.PurgeAsync(TimeSpan.FromHours(1));

        purged.Should().Be(1);
        (await queue.GetAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentDequeue_NeverClaimsSameJobTwice()
    {
        // Regression for CR-M018: without the DequeueAsync SemaphoreSlim, concurrent in-process
        // workers could both read the same Pending job and both mark it Processing.
        var queue = NewQueue();
        const int jobCount = 20;
        for (int i = 0; i < jobCount; i++)
        {
            await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        }

        var claimed = new ConcurrentBag<Guid>();
        var workers = Enumerable.Range(0, 6).Select(async _ =>
        {
            while (true)
            {
                var job = await queue.DequeueAsync();
                if (job == null) break;
                claimed.Add(job.Guid);
            }
        });
        await Task.WhenAll(workers);

        claimed.Should().HaveCount(jobCount);
        claimed.Distinct().Should().HaveCount(jobCount, "no job may be claimed by more than one worker");
    }

    // ── TASK-158 (SH-M021): two queues on one file share it ──

    [Fact]
    public async Task Two_queues_on_one_file_do_not_overwrite_each_others_jobs()
    {
        var a = NewQueue();
        var b = NewQueue();
        await a.EnqueueAsync(new JobDescriptor { JobType = "j1" });
        await b.EnqueueAsync(new JobDescriptor { JobType = "j2" });
        await a.EnqueueAsync(new JobDescriptor { JobType = "j3" });

        // A store of its own reads the file as another process would.
        var fresh = new Birko.Data.JSON.Stores.AsyncJsonStore<Birko.BackgroundJobs.JSON.Models.JsonJobDescriptorModel>();
        fresh.SetSettings(new Birko.Configuration.Settings(_location, "jobs"));
        var onDisk = await new JsonJobQueue(fresh, _clock).GetByStatusAsync(JobStatus.Pending);

        onDisk.Select(j => j.JobType).Should().BeEquivalentTo(new[] { "j1", "j2", "j3" });
    }

    [Fact]
    public async Task Two_queues_on_one_file_never_claim_the_same_job()
    {
        var a = NewQueue();
        var b = NewQueue();
        for (var i = 0; i < 20; i++) await a.EnqueueAsync(new JobDescriptor { JobType = "j" + i });

        var claimed = new ConcurrentBag<Guid>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(w => Task.Run(async () =>
        {
            var q = w % 2 == 0 ? a : b;
            while (await q.DequeueAsync() is { } job) claimed.Add(job.Guid);
        })));

        claimed.Should().HaveCount(20).And.OnlyHaveUniqueItems();
    }

}
