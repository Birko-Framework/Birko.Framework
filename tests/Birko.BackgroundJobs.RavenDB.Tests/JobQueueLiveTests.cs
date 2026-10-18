using System.Linq;
using System;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.RavenDB;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.BackgroundJobs.RavenDB.Tests;

/// <summary>
/// TASK-662 and TASK-158 on a live RavenDB: the attempt budget (MaxRetries null = the policy, 0 = no retry) and the
/// guarded transitions (complete / fail only a claimed job, cancel only a pending one) against the real server's native
/// filter update. Gated on <c>BIRKO_RAVEN_URL</c>; skipped without it, failed under <c>BIRKO_REQUIRE_LIVE</c>.
/// </summary>
public class JobQueueLiveTests(ITestOutputHelper output)
{
    private static readonly RetryPolicy Policy = new() { MaxRetries = 2, BaseDelay = TimeSpan.Zero, UseExponentialBackoff = false };

    private IJobQueue? Queue()
    {
        var server = Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
        if (string.IsNullOrWhiteSpace(server))
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_RAVEN_URL is not set but BIRKO_REQUIRE_LIVE is.");
            output.WriteLine("SKIPPED: BIRKO_RAVEN_URL not set.");
            return null;
        }
        return new RavenDBJobQueue(new Birko.Data.RavenDB.Stores.Settings(server, "birko_jobs_" + Guid.NewGuid().ToString("N")), Policy);
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
        (await queue.GetAsync(pending))!.Status.Should().Be(JobStatus.Pending, "an unclaimed job is not completed");
        (await queue.CancelAsync(pending)).Should().BeTrue();
        (await queue.CancelAsync(pending)).Should().BeFalse();

        var running = await queue.EnqueueAsync(new JobDescriptor { JobType = "r" });
        JobDescriptor? claimed = null;
        for (var i = 0; i < 40 && claimed == null; i++)
        {
            claimed = await queue.DequeueAsync();
            if (claimed == null) await Task.Delay(50);
        }
        claimed!.Guid.Should().Be(running);
        (await queue.CancelAsync(running)).Should().BeFalse("a running job is not cancelled");
        await queue.CompleteAsync(running);
        (await queue.GetAsync(running))!.Status.Should().Be(JobStatus.Completed);
    }

    // ── TASK-664: a job a dead worker left in Processing is reclaimed after the claim timeout ──

    private static readonly TimeSpan Task664Timeout = TimeSpan.FromMilliseconds(300);

    private static RavenDBJobQueue Task664Queue(RetryPolicy policy)
        => new(new Birko.Data.RavenDB.Stores.Settings(Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL")!, "birko_jobs_" + Guid.NewGuid().ToString("N")), policy, Task664Timeout);

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
}
