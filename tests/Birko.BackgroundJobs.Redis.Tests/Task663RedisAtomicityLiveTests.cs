using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Redis;
using Birko.Redis;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.Redis.Tests;

/// <summary>
/// TASK-663 on a live Redis (SH-M014..M018): a descriptor that is not pending is never dequeued, priority dominates among
/// due jobs, and concurrent transitions leave every job in exactly one status set with a status its claim allows.
/// Gated on <c>BIRKO_REDIS_HOST</c>; failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public class Task663RedisAtomicityLiveTests(ITestOutputHelper output)
{
    private static readonly RetryPolicy Policy = new() { MaxRetries = 5, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false };

    private (RedisJobQueue Queue, string Prefix)? Open()
    {
        var server = Environment.GetEnvironmentVariable("BIRKO_REDIS_HOST");
        if (string.IsNullOrWhiteSpace(server))
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REDIS_HOST is not set but BIRKO_REQUIRE_LIVE is.");
            output.WriteLine("SKIPPED: BIRKO_REDIS_HOST not set.");
            return null;
        }
        var prefix = "birko:jobs:" + Guid.NewGuid().ToString("N");
        return (new RedisJobQueue(new RedisSettings(server) { KeyPrefix = prefix }, Policy), prefix);
    }

    private static async Task<int[]> StatusSetsOf(RedisJobQueue queue, string prefix, Guid id)
    {
        var db = queue.ConnectionManager.GetDatabase();
        var hits = new List<int>();
        foreach (JobStatus s in Enum.GetValues(typeof(JobStatus)))
        {
            if (await db.SetContainsAsync($"{prefix}:status:{(int)s}", id.ToString())) hits.Add((int)s);
        }
        return hits.ToArray();
    }

    [Theory]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Dead)]
    [InlineData(JobStatus.Processing)]
    public async Task SH_M014_a_descriptor_that_is_not_pending_is_never_dequeued(JobStatus status)
    {
        if (Open() is not { } live) return;
        using var queue = live.Queue;
        await queue.EnqueueAsync(new JobDescriptor { JobType = "x", Status = status });
        (await queue.DequeueAsync()).Should().BeNull($"a {status} job is not runnable");
    }

    [Fact]
    public async Task SH_M015_a_higher_priority_job_enqueued_later_runs_first()
    {
        if (Open() is not { } live) return;
        using var queue = live.Queue;
        var low = await queue.EnqueueAsync(new JobDescriptor { JobType = "low", Priority = 0, EnqueuedAt = DateTime.UtcNow.AddSeconds(-30) });
        var high = await queue.EnqueueAsync(new JobDescriptor { JobType = "high", Priority = 5 });
        (await queue.DequeueAsync())!.Guid.Should().Be(high, "every backend drains the highest priority first among due jobs");
        (await queue.DequeueAsync())!.Guid.Should().Be(low);
    }

    [Fact]
    public async Task SH_M015_a_negative_priority_runs_after_the_default()
    {
        if (Open() is not { } live) return;
        using var queue = live.Queue;
        var background = await queue.EnqueueAsync(new JobDescriptor { JobType = "bg", Priority = -1, EnqueuedAt = DateTime.UtcNow.AddSeconds(-30) });
        var normal = await queue.EnqueueAsync(new JobDescriptor { JobType = "n" });
        (await queue.DequeueAsync())!.Guid.Should().Be(normal);
        (await queue.DequeueAsync())!.Guid.Should().Be(background);
    }

    [Fact]
    public async Task SH_M015_a_future_high_priority_job_still_waits()
    {
        if (Open() is not { } live) return;
        using var queue = live.Queue;
        await queue.EnqueueAsync(new JobDescriptor { JobType = "later", Priority = 999, ScheduledAt = DateTime.UtcNow.AddMinutes(5) });
        var now = await queue.EnqueueAsync(new JobDescriptor { JobType = "now", Priority = 0 });
        (await queue.DequeueAsync())!.Guid.Should().Be(now);
        (await queue.DequeueAsync()).Should().BeNull("the high-priority job is not due");
    }

    // Upgrade: before TASK-663 a job sat in {prefix}:queue:{name} scored as (due ticks / 1e4) - priority bonus (< 1).
    // That key is now the delayed set on the same scale, so such a job is promoted when due and served.
    [Fact]
    public async Task A_job_stored_by_the_old_layout_is_still_served()
    {
        if (Open() is not { } live) return;
        var (queue, prefix) = live;
        using var _ = queue;
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "old", Priority = 3 });
        var db = queue.ConnectionManager.GetDatabase();
        var oldScore = DateTime.UtcNow.AddSeconds(-1).Ticks / 1e4 - 3 * (0.999 / 999.0);
        await db.SortedSetAddAsync($"{prefix}:queue:default", id.ToString(), oldScore);

        (await queue.DequeueAsync())!.Guid.Should().Be(id);
    }

    [Fact]
    public async Task SH_M017_M018_concurrent_cancel_and_dequeue_never_both_win_and_status_sets_stay_disjoint()
    {
        if (Open() is not { } live) return;
        var (queue, prefix) = live;
        using var _ = queue;
        const int jobs = 60;
        var ids = new Guid[jobs];
        for (var i = 0; i < jobs; i++) ids[i] = await queue.EnqueueAsync(new JobDescriptor { JobType = "j" });

        var claimed = new ConcurrentBag<Guid>();
        var cancelled = new ConcurrentBag<Guid>();
        using var start = new ManualResetEventSlim(false);
        var dequeuers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            start.Wait();
            for (var k = 0; k < jobs; k++)
            {
                if (await queue.DequeueAsync() is { } j) claimed.Add(j.Guid);
            }
        }));
        var cancellers = Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
        {
            start.Wait();
            foreach (var id in ids.Where((_, i) => i % 4 == w))
            {
                if (await queue.CancelAsync(id)) cancelled.Add(id);
            }
        }));
        var all = dequeuers.Concat(cancellers).ToArray();
        start.Set();
        await Task.WhenAll(all);

        output.WriteLine($"claimed {claimed.Count}, cancelled {cancelled.Count}");
        claimed.Should().OnlyHaveUniqueItems();
        claimed.Intersect(cancelled).Should().BeEmpty("a job is either claimed or cancelled, never both");
        foreach (var id in ids)
        {
            var job = (await queue.GetAsync(id))!;
            (await StatusSetsOf(queue, prefix, id)).Should().Equal(new[] { (int)job.Status }, $"job {id} is listed under exactly its own status");
            job.Status.Should().Be(claimed.Contains(id) ? JobStatus.Processing : JobStatus.Cancelled);
        }
    }

    [Fact]
    public async Task SH_M017_concurrent_complete_and_fail_leave_one_status()
    {
        if (Open() is not { } live) return;
        var (queue, prefix) = live;
        using var _ = queue;
        const int jobs = 40;
        for (var i = 0; i < jobs; i++) await queue.EnqueueAsync(new JobDescriptor { JobType = "j" });
        var running = new List<Guid>();
        for (var i = 0; i < jobs; i++) running.Add((await queue.DequeueAsync())!.Guid);

        await Task.WhenAll(running.SelectMany(id => new[]
        {
            Task.Run(() => queue.CompleteAsync(id)),
            Task.Run(() => queue.FailAsync(id, "boom")),
        }));

        var mixed = 0;
        foreach (var id in running)
        {
            var job = (await queue.GetAsync(id))!;
            job.Status.Should().BeOneOf(JobStatus.Completed, JobStatus.Scheduled);
            (await StatusSetsOf(queue, prefix, id)).Should().Equal(new[] { (int)job.Status }, $"job {id} is listed once");
            if (job.Status == JobStatus.Completed && job.LastError != null) mixed++;
        }
        mixed.Should().Be(0, "a fail that lost to the complete writes nothing");
    }
}
