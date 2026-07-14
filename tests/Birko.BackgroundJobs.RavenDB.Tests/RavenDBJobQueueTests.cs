using Birko.BackgroundJobs;
using Birko.BackgroundJobs.RavenDB;
using Birko.BackgroundJobs.RavenDB.Models;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.RavenDB.Tests;

/// <summary>
/// Offline tests for the RavenDB job queue (over an in-memory store double):
///  - happy-path enqueue -> dequeue (Processing, AttemptCount 1) -> complete (Completed);
///  - dequeue on an empty queue returns null;
///  - FailAsync reschedules with backoff while retries remain, and goes terminal Dead on exhaustion;
///  - PurgeAsync purges terminal statuses (Completed | Dead | Cancelled) but never a retryable Scheduled;
///  - CR-M021 regression: DequeueAsync's ClaimToken conditional-update + re-read-verify hands out a
///    distinct job per claim (three sequential dequeues over three enqueued jobs return three distinct
///    Processing jobs; the fourth returns null).
/// </summary>
public class RavenDBJobQueueTests
{
    private static RavenDBJobQueue NewQueue() =>
        new(new InMemoryRavenStore<RavenJobDescriptorModel>());

    private static async Task<Guid> EnqueueAndDequeue(RavenDBJobQueue queue, int maxRetries)
    {
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = maxRetries });
        await queue.DequeueAsync(); // moves to Processing, AttemptCount -> 1
        return id;
    }

    [Fact]
    public async Task Enqueue_Dequeue_Complete_HappyPath()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = 3 });

        var dequeued = await queue.DequeueAsync();
        dequeued.Should().NotBeNull();
        dequeued!.Id.Should().Be(id);
        dequeued.Status.Should().Be(JobStatus.Processing);
        dequeued.AttemptCount.Should().Be(1);

        await queue.CompleteAsync(id);

        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Completed);
        job.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DequeueAsync_OnEmptyQueue_ReturnsNull()
    {
        var queue = NewQueue();

        var dequeued = await queue.DequeueAsync();

        dequeued.Should().BeNull();
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
    public async Task FailAsync_JobMaxRetriesZero_FallsBackToDefaultPolicy()
    {
        // Regression for CR-L029: a job with MaxRetries == 0 went straight to Dead, ignoring the
        // queue's RetryPolicy. NewQueue() uses the default policy (MaxRetries = 3), so a first failure
        // must reschedule rather than die.
        var queue = NewQueue();
        var id = await EnqueueAndDequeue(queue, maxRetries: 0); // AttemptCount -> 1, below default policy's 3

        await queue.FailAsync(id, "boom");

        var job = await queue.GetAsync(id);
        job!.Status.Should().Be(JobStatus.Scheduled);
        job.ScheduledAt.Should().NotBeNull();
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
    public async Task PurgeAsync_RemovesTerminalButNotRetryableScheduled()
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

    [Fact]
    public async Task DequeueAsync_AtomicClaim_HandsOutDistinctJobs()
    {
        var queue = NewQueue();

        var ids = new[]
        {
            await queue.EnqueueAsync(new JobDescriptor { JobType = "t" }),
            await queue.EnqueueAsync(new JobDescriptor { JobType = "t" }),
            await queue.EnqueueAsync(new JobDescriptor { JobType = "t" }),
        };

        var claimed = new List<Guid>();
        for (int i = 0; i < 3; i++)
        {
            var job = await queue.DequeueAsync();
            job.Should().NotBeNull("three enqueued jobs must each be claimable");
            job!.Status.Should().Be(JobStatus.Processing);
            claimed.Add(job.Id);
        }

        claimed.Should().OnlyHaveUniqueItems("each claim must hand out a distinct job (CR-M021)");
        claimed.Should().BeEquivalentTo(ids, "every enqueued job is claimed exactly once");

        (await queue.DequeueAsync()).Should().BeNull("no eligible jobs remain after all three are claimed");
    }
}
