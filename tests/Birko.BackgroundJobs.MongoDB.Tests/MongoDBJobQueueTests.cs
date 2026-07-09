using Birko.BackgroundJobs;
using Birko.BackgroundJobs.MongoDB;
using Birko.BackgroundJobs.MongoDB.Models;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.MongoDB.Tests;

/// <summary>
/// Offline regressions for the MongoDB job queue, mirroring the Cosmos suite:
///  - Lifecycle: Enqueue -> Dequeue (Processing, AttemptCount 1) -> Complete -> Completed.
///  - FailAsync on retry exhaustion sets the terminal Dead status (+ CompletedAt), and while
///    retries remain reschedules with backoff (Scheduled + future ScheduledAt).
///  - PurgeAsync purges terminal statuses (Completed | Dead | Cancelled), never a retryable Scheduled.
///  - CR-M020 atomic claim: three sequential Dequeues over three enqueued jobs return three distinct
///    ids (each Processing) and a fourth returns null, exercising the ClaimToken conditional-update +
///    re-read-verify loop.
/// </summary>
public class MongoDBJobQueueTests
{
    private static MongoDBJobQueue NewQueue() =>
        new(new InMemoryMongoStore<MongoJobDescriptorModel>());

    private static async Task<Guid> EnqueueAndDequeue(MongoDBJobQueue queue, int maxRetries)
    {
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", MaxRetries = maxRetries });
        await queue.DequeueAsync(); // moves to Processing, AttemptCount -> 1
        return id;
    }

    [Fact]
    public async Task Lifecycle_EnqueueDequeueComplete()
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
        (await queue.DequeueAsync()).Should().BeNull();
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
    public async Task PurgeAsync_RemovesTerminalButNotRetryable()
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
    public async Task DequeueAsync_ClaimsEachJobExactlyOnce()
    {
        var queue = NewQueue();

        var id1 = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        var id2 = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });
        var id3 = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });

        var d1 = await queue.DequeueAsync();
        var d2 = await queue.DequeueAsync();
        var d3 = await queue.DequeueAsync();
        var d4 = await queue.DequeueAsync();

        d1.Should().NotBeNull();
        d2.Should().NotBeNull();
        d3.Should().NotBeNull();
        d4.Should().BeNull("only three jobs were enqueued");

        d1!.Status.Should().Be(JobStatus.Processing);
        d2!.Status.Should().Be(JobStatus.Processing);
        d3!.Status.Should().Be(JobStatus.Processing);

        var claimed = new[] { d1.Id, d2.Id, d3.Id };
        claimed.Should().OnlyHaveUniqueItems("the atomic claim must hand each job to exactly one dequeue");
        claimed.Should().BeEquivalentTo(new[] { id1, id2, id3 });
    }
}
