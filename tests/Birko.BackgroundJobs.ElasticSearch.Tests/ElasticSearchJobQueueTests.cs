using Birko.BackgroundJobs;
using Birko.BackgroundJobs.ElasticSearch.Models;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.ElasticSearch.Tests;

/// <summary>
/// Offline regressions for the Elasticsearch job queue (CR-M017 added this test project):
///  - Enqueue → Dequeue moves a job to Processing (AttemptCount 1) → Complete sets Completed.
///  - Dequeue on an empty queue returns null.
///  - FailAsync reschedules with backoff while retries remain (Scheduled + future ScheduledAt),
///    and sets the terminal Dead status (+ CompletedAt) on retry exhaustion.
///  - PurgeAsync purges terminal statuses (Completed / Dead), never a retryable Scheduled job.
///  - CR-M016 atomic claim: DequeueAsync uses a ClaimToken conditional-update + re-read-verify, so
///    concurrent/sequential dequeues never hand out the same job twice.
/// </summary>
public class ElasticSearchJobQueueTests
{
    private static ElasticSearchJobQueue NewQueue() =>
        new(new InMemoryElasticStore<ElasticJobDescriptorModel>());

    private static async Task<Guid> EnqueueAndDequeue(ElasticSearchJobQueue queue, int maxRetries)
    {
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = maxRetries });
        await queue.DequeueAsync(); // moves to Processing, AttemptCount -> 1
        return id;
    }

    [Fact]
    public async Task EnqueueDequeue_MovesToProcessing_ThenCompleteSetsCompleted()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });

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
    public async Task DequeueAsync_WhenEmpty_ReturnsNull()
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
    public async Task DequeueAsync_ThreeSequentialClaims_ReturnDistinctProcessingJobs()
    {
        // CR-M016: the atomic ClaimToken conditional-update + re-read-verify must never
        // hand out the same job to two dequeues.
        var queue = NewQueue();

        var enqueued = new HashSet<Guid>
        {
            await queue.EnqueueAsync(new JobDescriptor { JobType = "t" }),
            await queue.EnqueueAsync(new JobDescriptor { JobType = "t" }),
            await queue.EnqueueAsync(new JobDescriptor { JobType = "t" }),
        };

        var claimed = new List<JobDescriptor>();
        for (int i = 0; i < 3; i++)
        {
            var job = await queue.DequeueAsync();
            job.Should().NotBeNull();
            job!.Status.Should().Be(JobStatus.Processing);
            claimed.Add(job);
        }

        claimed.Select(j => j.Id).Distinct().Should().HaveCount(3, "each dequeue must claim a distinct job");
        claimed.Select(j => j.Id).Should().BeEquivalentTo(enqueued, "all three enqueued jobs are claimed exactly once");

        // Queue now drained.
        (await queue.DequeueAsync()).Should().BeNull();
    }
}
