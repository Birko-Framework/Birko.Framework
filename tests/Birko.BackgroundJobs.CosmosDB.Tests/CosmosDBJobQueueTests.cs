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

        // A completed job — should be purged.
        var doneId = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
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
}
