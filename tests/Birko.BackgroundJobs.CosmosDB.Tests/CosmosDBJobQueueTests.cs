using System.Linq;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.CosmosDB;
using Birko.BackgroundJobs.CosmosDB.Models;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.CosmosDB.Tests;

/// <summary>
/// Regressions for CR-H008 / CR-H009 in the Cosmos job queue:
///  - FailAsync on retry exhaustion must set the terminal Dead status (not the retryable Failed),
///    and while retries remain must reschedule with backoff (Scheduled + future ScheduledAt).
///  - PurgeAsync must purge terminal statuses (Completed | Dead | Cancelled), never Failed.
/// </summary>
public class CosmosDBJobQueueTests
{
    private static CosmosDBJobQueue NewQueue() =>
        new(new InMemoryCosmosStore<CosmosJobDescriptorModel>());

    private static async Task<Guid> EnqueueAndDequeue(CosmosDBJobQueue queue, int maxRetries)
    {
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = maxRetries });
        await queue.DequeueAsync(); // moves to Processing, AttemptCount -> 1
        return id;
    }

    [Fact]
    public async Task FailAsync_OnRetryExhaustion_SetsDead()
    {
        var queue = NewQueue();
        var id = await EnqueueAndDequeue(queue, maxRetries: 1); // AttemptCount becomes 1 >= 1

        await queue.FailAsync(id, "boom");

        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Dead);
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task FailAsync_WithRetriesRemaining_ReschedulesWithBackoff()
    {
        var queue = NewQueue();
        var before = DateTime.UtcNow;
        var id = await EnqueueAndDequeue(queue, maxRetries: 5); // AttemptCount 1 < 5

        await queue.FailAsync(id, "transient");

        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Scheduled);
        job.ScheduledAt.Should().NotBeNull();
        job.ScheduledAt!.Value.Should().BeAfter(before, "the retry must be delayed by the backoff policy");
    }

    [Fact]
    public async Task PurgeAsync_RemovesDeadButNotFailedRetryable()
    {
        var queue = NewQueue();

        // A dead job (exhausted) — should be purged.
        var deadId = await EnqueueAndDequeue(queue, maxRetries: 1);
        await queue.FailAsync(deadId, "dead");

        // A completed job — should be purged. Claimed first: only a claimed job completes (TASK-158 / TASK-661).
        var doneId = await EnqueueAndDequeue(queue, maxRetries: 3);
        await queue.CompleteAsync(doneId);

        // A retryable (Scheduled) job — must NOT be purged.
        var retryId = await EnqueueAndDequeue(queue, maxRetries: 5);
        await queue.FailAsync(retryId, "retry");

        var purged = await queue.PurgeAsync(TimeSpan.FromTicks(-1)); // cutoff in the future -> all terminal jobs qualify

        purged.Should().Be(2);
        (await queue.GetAsync(deadId)).Should().BeNull();
        (await queue.GetAsync(doneId)).Should().BeNull();
        (await queue.GetAsync(retryId)).Should().NotBeNull("a job with retries pending is not terminal");
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
        var queue = new CosmosDBJobQueue(new InMemoryCosmosStore<CosmosJobDescriptorModel>(), Task662Policy);
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

    // ── TASK-661: claims and transitions are compare-and-set ──

    [Fact]
    public async Task TASK_661_concurrent_dequeuers_claim_each_job_once()
    {
        var queue = NewQueue();
        const int jobs = 20;
        for (var i = 0; i < jobs; i++) await queue.EnqueueAsync(new JobDescriptor { JobType = "j" + i });

        var claims = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        using var start = new ManualResetEventSlim(false);
        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            start.Wait();
            for (var k = 0; k < 6; k++)
            {
                var job = await queue.DequeueAsync();
                if (job != null) claims.Add(job.Guid);
            }
        })).ToArray();
        start.Set();
        await Task.WhenAll(workers);

        claims.Should().OnlyHaveUniqueItems();
        claims.Should().HaveCount(jobs);
    }

    [Fact]
    public async Task TASK_661_cancel_acts_only_on_a_job_that_has_not_started()
    {
        var queue = NewQueue();
        var running = await EnqueueAndDequeue(queue, maxRetries: 3);
        (await queue.CancelAsync(running)).Should().BeFalse("a running job is not cancelled");
        (await queue.GetAsync(running))!.Status.Should().Be(JobStatus.Processing);

        await queue.CompleteAsync(running);
        (await queue.CancelAsync(running)).Should().BeFalse("a completed job is not cancelled");
        (await queue.GetAsync(running))!.Status.Should().Be(JobStatus.Completed);

        var pending = await queue.EnqueueAsync(new JobDescriptor { JobType = "p" });
        (await queue.CancelAsync(pending)).Should().BeTrue();
        (await queue.CancelAsync(pending)).Should().BeFalse();
    }

    [Fact]
    public async Task TASK_661_complete_and_fail_act_only_on_a_claimed_job()
    {
        var queue = NewQueue();
        var pending = await queue.EnqueueAsync(new JobDescriptor { JobType = "p" });
        await queue.CompleteAsync(pending);
        await queue.FailAsync(pending, "x");
        var job = await queue.GetAsync(pending);
        job!.Status.Should().Be(JobStatus.Pending);
        job.LastError.Should().BeNull();
    }

    // ── TASK-664: a job a dead worker left in Processing is reclaimed after the claim timeout ──

    private static readonly TimeSpan Task664Timeout = TimeSpan.FromMilliseconds(300);

    private static CosmosDBJobQueue Task664Queue(RetryPolicy policy) => new(new InMemoryCosmosStore<CosmosJobDescriptorModel>(), policy, Task664Timeout);

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

    [Fact]
    public async Task TASK_664_racing_workers_reclaim_an_abandoned_job_once()
    {
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
