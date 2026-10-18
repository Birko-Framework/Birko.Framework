using System.Linq;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Redis;
using Birko.Redis;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.Redis.Tests;

/// <summary>
/// TASK-662, TASK-158 and TASK-663 on a live Redis: the attempt budget (MaxRetries null = the policy, 0 = no retry) and the
/// guarded transitions (complete / fail only a claimed job, cancel only a pending one) against the real server's native
/// filter update. Gated on <c>BIRKO_REDIS_HOST</c>; skipped without it, failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public class JobQueueLiveTests(ITestOutputHelper output)
{
    private static readonly RetryPolicy Policy = new() { MaxRetries = 2, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false };

    private IJobQueue? Queue()
    {
        var server = Environment.GetEnvironmentVariable("BIRKO_REDIS_HOST");
        if (string.IsNullOrWhiteSpace(server))
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REDIS_HOST is not set but BIRKO_REQUIRE_LIVE is.");
            output.WriteLine("SKIPPED: BIRKO_REDIS_HOST not set.");
            return null;
        }
        return new RedisJobQueue(new RedisSettings(server) { KeyPrefix = "birko:jobs:" + Guid.NewGuid().ToString("N") }, Policy);
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    public async Task The_attempt_budget_is_the_jobs_own_or_else_the_policys(int? maxRetries, int attempts)
    {
        if (Queue() is not { } queue) return;
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = maxRetries });
        (await queue.GetAsync(id))!.MaxRetries.Should().Be(maxRetries);

        var claims = 0;
        for (var i = 0; i < 80 && claims < 10; i++)
        {
            var job = await queue.DequeueAsync();
            if (job == null)
            {
                if ((await queue.GetAsync(id))!.Status == JobStatus.Dead) break;
                await Task.Delay(50);
                continue;
            }
            claims++;
            await queue.FailAsync(job.Guid, "boom");
        }

        claims.Should().Be(attempts);
        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Dead);
    }

    [Fact]
    public async Task Transitions_act_only_on_the_status_they_belong_to()
    {
        if (Queue() is not { } queue) return;
        var pending = await queue.EnqueueAsync(new JobDescriptor { JobType = "p" });
        await queue.CompleteAsync(pending);
        await queue.FailAsync(pending, "x");
        (await queue.GetAsync(pending))!.Status.Should().Be(JobStatus.Pending, "an unclaimed job is neither completed nor failed");
        (await queue.CancelAsync(pending)).Should().BeTrue();
        (await queue.CancelAsync(pending)).Should().BeFalse();

        var running = await queue.EnqueueAsync(new JobDescriptor { JobType = "r" });
        (await queue.DequeueAsync())!.Guid.Should().Be(running);
        (await queue.CancelAsync(running)).Should().BeFalse("a running job is not cancelled");
        await queue.CompleteAsync(running);
        (await queue.GetAsync(running))!.Status.Should().Be(JobStatus.Completed);
        (await queue.CancelAsync(running)).Should().BeFalse();
    }

    // ── TASK-664: a job a dead worker left in Processing is reclaimed after the claim timeout ──

    private static readonly TimeSpan Task664Timeout = TimeSpan.FromMilliseconds(300);

    private static RedisJobQueue Task664Queue(RetryPolicy policy)
        => new(new RedisSettings(Environment.GetEnvironmentVariable("BIRKO_REDIS_HOST")!) { KeyPrefix = "birko:jobs:" + Guid.NewGuid().ToString("N") }, policy, claimTimeout: Task664Timeout);

    [Fact]
    public async Task TASK_664_an_abandoned_job_runs_again_after_the_claim_timeout()
    {
        if (Queue() is null) return;
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
        if (Queue() is null) return;
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

    [Fact]
    public async Task TASK_664_racing_workers_reclaim_an_abandoned_job_once()
    {
        if (Queue() is null) return;
        var queue = Task664Queue(new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false });
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        (await queue.DequeueAsync())!.Guid.Should().Be(id);
        await Task.Delay(Task664Timeout * 2);
        using var start = new System.Threading.ManualResetEventSlim(false);
        var workers = System.Linq.Enumerable.Range(0, 6).Select(_ => Task.Run(async () => { start.Wait(); return await queue.DequeueAsync(); })).ToArray();
        start.Set();
        var got = await Task.WhenAll(workers);
        got.Count(j => j != null).Should().Be(1, "one worker wins the reclaim");
        (await queue.GetAsync(id))!.AttemptCount.Should().Be(2);
    }

    // ── TASK-175 (SH-L013): a Delay on an enqueued descriptor is honoured, not dropped ──

    [Fact]
    public async Task TASK_175_SH_L013_a_delay_on_an_enqueued_descriptor_is_honoured()
    {
        if (Queue() is null) return;
        var queue = Task664Queue(new RetryPolicy());
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", Delay = TimeSpan.FromHours(1) });
        (await queue.DequeueAsync()).Should().BeNull("the job is not due for an hour");
        var stored = (await queue.GetAsync(id))!;
        stored.Status.Should().Be(JobStatus.Scheduled);
        stored.ScheduledAt.Should().BeCloseTo(stored.EnqueuedAt.AddHours(1), TimeSpan.FromSeconds(1));
    }

    // ── TASK-175: SH-L015 / SH-L016 / SH-L017 ──

    private static (RedisJobQueue Queue, string Prefix) Task175Queue()
    {
        var prefix = "birko:jobs:" + Guid.NewGuid().ToString("N");
        return (new RedisJobQueue(new RedisSettings(Environment.GetEnvironmentVariable("BIRKO_REDIS_HOST")!) { KeyPrefix = prefix }), prefix);
    }

    [Fact]
    public async Task TASK_175_SH_L015_a_hash_missing_a_required_field_is_named()
    {
        if (Queue() is null) return;
        var (queue, prefix) = Task175Queue();
        using var _ = queue;
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        await queue.ConnectionManager.GetDatabase().HashDeleteAsync($"{prefix}:job:{id}", "AttemptCount");

        var read = () => queue.GetAsync(id);
        (await read.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("AttemptCount").And.Contain(id.ToString());
    }

    [Fact]
    public async Task TASK_175_SH_L016_a_queue_member_whose_hash_is_gone_does_not_stop_the_poll()
    {
        if (Queue() is null) return;
        var (queue, prefix) = Task175Queue();
        using var _ = queue;
        var db = queue.ConnectionManager.GetDatabase();
        var orphan = Guid.NewGuid().ToString();
        await db.SortedSetAddAsync($"{prefix}:queue:default", orphan, DateTime.UtcNow.AddSeconds(-10).Ticks / 1e4);
        var real = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });

        (await queue.DequeueAsync())!.Guid.Should().Be(real, "the orphan is dropped and the poll moves on (TASK-663)");
    }

    [Fact]
    public async Task TASK_175_SH_L017_status_listing_returns_the_newest_within_the_limit()
    {
        if (Queue() is null) return;
        var (queue, _) = Task175Queue();
        using var __ = queue;
        var ids = new List<Guid>();
        for (var i = 0; i < 30; i++)
            ids.Add(await queue.EnqueueAsync(new JobDescriptor { JobType = "t", EnqueuedAt = DateTime.UtcNow.AddMinutes(i) }));

        var newest = await queue.GetByStatusAsync(JobStatus.Pending, 5);
        newest.Select(j => j.Guid).Should().Equal(ids.AsEnumerable().Reverse().Take(5));
    }
}
