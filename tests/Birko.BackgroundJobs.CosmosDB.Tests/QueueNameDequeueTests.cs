using Birko.BackgroundJobs;
using Birko.BackgroundJobs.CosmosDB;
using Birko.BackgroundJobs.CosmosDB.Models;
using Birko.BackgroundJobs.Processing;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.CosmosDB.Tests;

/// <summary>
/// SH-H001 (TASK-317) — an unnamed job belongs to every queue. <c>JobDispatcher.EnqueueAsync</c> /
/// <c>ScheduleAsync</c> / <c>EnqueueWithPriorityAsync</c> leave <c>QueueName</c> null, and
/// <c>BackgroundJobProcessor</c> always dequeues <c>JobQueueOptions.DefaultQueueName</c> ("default"). The Cosmos
/// queue filtered a named dequeue to <c>j.QueueName == queueName</c> alone, so on Cosmos <b>no dispatcher-enqueued
/// job ever ran</b>: it sat in Pending forever, and <c>PurgeAsync</c> never removed it because it never became
/// terminal. Every sibling backend admits <c>j.QueueName == null</c>; this pins Cosmos to the same contract.
///
/// <para>The assertions are on observed state — the job that came back and its stored status — never on the
/// absence of an exception. The store double compiles the filter in memory, so these tests pin the predicate's
/// meaning; that Cosmos SQL agrees rests on the serializer writing <c>"QueueName": null</c> explicitly.</para>
/// </summary>
public class QueueNameDequeueTests
{
    private sealed class NoopJob : IJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static CosmosDBJobQueue NewQueue() =>
        new(new InMemoryCosmosStore<CosmosJobDescriptorModel>());

    [Fact]
    public async Task A_dispatcher_enqueued_job_is_dequeued_by_the_processors_default_queue()
    {
        var queue = NewQueue();
        var dispatcher = new JobDispatcher(queue, new SystemDateTimeProvider());
        var id = await dispatcher.EnqueueAsync<NoopJob>();

        var dequeued = await queue.DequeueAsync(new JobQueueOptions().DefaultQueueName);

        dequeued.Should().NotBeNull("an unnamed job must reach the queue the processor polls");
        dequeued!.Guid.Should().Be(id);
        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task An_unnamed_job_is_dequeued_by_any_named_queue()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t" });

        var dequeued = await queue.DequeueAsync("reports");

        dequeued!.Guid.Should().Be(id);
        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task A_due_scheduled_unnamed_job_is_dequeued_by_a_named_queue()
    {
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor
        {
            JobType = "t",
            Status = JobStatus.Scheduled,
            ScheduledAt = DateTime.UtcNow.AddMinutes(-1)
        });

        (await queue.DequeueAsync("default"))!.Guid.Should().Be(id);
    }

    [Fact]
    public async Task A_named_queue_does_not_take_another_queues_job()
    {
        // Contract pin: admitting unnamed jobs must not admit every job.
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", QueueName = "emails" });

        (await queue.DequeueAsync("reports")).Should().BeNull();
        (await queue.GetAsync(id))!.Status.Should().Be(JobStatus.Pending);
    }

    [Fact]
    public async Task A_named_queue_takes_its_own_job()
    {
        // Contract pin: the path that already worked.
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", QueueName = "emails" });

        (await queue.DequeueAsync("emails"))!.Guid.Should().Be(id);
    }

    [Fact]
    public async Task An_unnamed_dequeue_takes_any_job()
    {
        // Contract pin: queueName null means "any queue", as on every sibling.
        var queue = NewQueue();
        var id = await queue.EnqueueAsync(new JobDescriptor { JobType = "t", QueueName = "emails" });

        (await queue.DequeueAsync())!.Guid.Should().Be(id);
    }
}
