using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.Time;

namespace Birko.BackgroundJobs.Tests.Processing
{
    public class InMemoryJobQueueTests
    {
        private readonly IDateTimeProvider _clock = new SystemDateTimeProvider();
        private readonly InMemoryJobQueue _queue;

        public InMemoryJobQueueTests()
        {
            _queue = new InMemoryJobQueue(_clock);
        }

        private static JobDescriptor CreateDescriptor(string jobType = "TestJob", int priority = 0, string? queueName = null)
        {
            return new JobDescriptor
            {
                JobType = jobType,
                Priority = priority,
                QueueName = queueName
            };
        }

        [Fact]
        public async Task EnqueueAsync_ReturnsJobId()
        {
            var descriptor = CreateDescriptor();

            var id = await _queue.EnqueueAsync(descriptor);

            id.Should().Be(descriptor.Id);
        }

        [Fact]
        public async Task DequeueAsync_ReturnsEnqueuedJob()
        {
            var descriptor = CreateDescriptor();
            await _queue.EnqueueAsync(descriptor);

            var result = await _queue.DequeueAsync();

            result.Should().NotBeNull();
            result!.Id.Should().Be(descriptor.Id);
            result.Status.Should().Be(JobStatus.Processing);
            result.AttemptCount.Should().Be(1);
            result.LastAttemptAt.Should().NotBeNull();
        }

        [Fact]
        public async Task DequeueAsync_EmptyQueue_ReturnsNull()
        {
            var result = await _queue.DequeueAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task DequeueAsync_HigherPriorityFirst()
        {
            var low = CreateDescriptor("LowJob", priority: 0);
            var high = CreateDescriptor("HighJob", priority: 10);

            await _queue.EnqueueAsync(low);
            await _queue.EnqueueAsync(high);

            var first = await _queue.DequeueAsync();

            first.Should().NotBeNull();
            first!.Id.Should().Be(high.Id);
        }

        [Fact]
        public async Task DequeueAsync_SamePriority_FIFO()
        {
            var first = CreateDescriptor("First");
            first.EnqueuedAt = DateTime.UtcNow.AddSeconds(-2);
            var second = CreateDescriptor("Second");
            second.EnqueuedAt = DateTime.UtcNow;

            await _queue.EnqueueAsync(first);
            await _queue.EnqueueAsync(second);

            var dequeued = await _queue.DequeueAsync();

            dequeued.Should().NotBeNull();
            dequeued!.Id.Should().Be(first.Id);
        }

        [Fact]
        public async Task DequeueAsync_SkipsProcessingJobs()
        {
            var descriptor = CreateDescriptor();
            await _queue.EnqueueAsync(descriptor);

            // First dequeue sets it to Processing
            await _queue.DequeueAsync();

            // Second dequeue should find nothing
            var result = await _queue.DequeueAsync();
            result.Should().BeNull();
        }

        [Fact]
        public async Task DequeueAsync_ScheduledJob_NotReadyYet()
        {
            var descriptor = CreateDescriptor();
            descriptor.Status = JobStatus.Scheduled;
            descriptor.ScheduledAt = DateTime.UtcNow.AddHours(1);
            await _queue.EnqueueAsync(descriptor);

            var result = await _queue.DequeueAsync();

            result.Should().BeNull();
        }

        [Fact]
        public async Task DequeueAsync_ScheduledJob_ReadyNow()
        {
            var descriptor = CreateDescriptor();
            descriptor.Status = JobStatus.Scheduled;
            descriptor.ScheduledAt = DateTime.UtcNow.AddSeconds(-1);
            await _queue.EnqueueAsync(descriptor);

            var result = await _queue.DequeueAsync();

            result.Should().NotBeNull();
            result!.Id.Should().Be(descriptor.Id);
        }

        [Fact]
        public async Task DequeueAsync_FiltersByQueueName()
        {
            var highPri = CreateDescriptor("HighPri", queueName: "high");
            var defaultJob = CreateDescriptor("Default", queueName: "default");

            await _queue.EnqueueAsync(highPri);
            await _queue.EnqueueAsync(defaultJob);

            var result = await _queue.DequeueAsync("high");

            result.Should().NotBeNull();
            result!.Id.Should().Be(highPri.Id);
        }

        [Fact]
        public async Task CompleteAsync_SetsCompletedStatus()
        {
            var descriptor = CreateDescriptor();
            await _queue.EnqueueAsync(descriptor);
            await _queue.DequeueAsync();

            await _queue.CompleteAsync(descriptor.Id);

            var job = await _queue.GetAsync(descriptor.Id);
            job.Should().NotBeNull();
            job!.Status.Should().Be(JobStatus.Completed);
            job.CompletedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task FailAsync_WithRetriesRemaining_SchedulesRetry()
        {
            var descriptor = CreateDescriptor();
            descriptor.MaxRetries = 3;
            await _queue.EnqueueAsync(descriptor);
            await _queue.DequeueAsync(); // AttemptCount = 1

            await _queue.FailAsync(descriptor.Id, "Connection timeout");

            var job = await _queue.GetAsync(descriptor.Id);
            job.Should().NotBeNull();
            job!.Status.Should().Be(JobStatus.Scheduled);
            job.ScheduledAt.Should().NotBeNull();
            job.LastError.Should().Be("Connection timeout");
        }

        [Fact]
        public async Task FailAsync_NoRetriesRemaining_MarksDead()
        {
            var queue = new InMemoryJobQueue(_clock, RetryPolicy.None);
            var descriptor = CreateDescriptor();
            descriptor.MaxRetries = 0;
            await queue.EnqueueAsync(descriptor);
            await queue.DequeueAsync(); // AttemptCount = 1

            await queue.FailAsync(descriptor.Id, "Permanent failure");

            var job = await queue.GetAsync(descriptor.Id);
            job.Should().NotBeNull();
            job!.Status.Should().Be(JobStatus.Dead);
            job.CompletedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task CancelAsync_PendingJob_ReturnsTrueAndCancels()
        {
            var descriptor = CreateDescriptor();
            await _queue.EnqueueAsync(descriptor);

            var result = await _queue.CancelAsync(descriptor.Id);

            result.Should().BeTrue();
            var job = await _queue.GetAsync(descriptor.Id);
            job!.Status.Should().Be(JobStatus.Cancelled);
            job.CompletedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task CancelAsync_ProcessingJob_ReturnsFalse()
        {
            var descriptor = CreateDescriptor();
            await _queue.EnqueueAsync(descriptor);
            await _queue.DequeueAsync(); // now Processing

            var result = await _queue.CancelAsync(descriptor.Id);

            result.Should().BeFalse();
        }

        [Fact]
        public async Task CancelAsync_NonexistentJob_ReturnsFalse()
        {
            var result = await _queue.CancelAsync(Guid.NewGuid());

            result.Should().BeFalse();
        }

        [Fact]
        public async Task GetAsync_ExistingJob_ReturnsDescriptor()
        {
            var descriptor = CreateDescriptor();
            await _queue.EnqueueAsync(descriptor);

            var result = await _queue.GetAsync(descriptor.Id);

            result.Should().NotBeNull();
            result!.Id.Should().Be(descriptor.Id);
        }

        [Fact]
        public async Task GetAsync_NonexistentJob_ReturnsNull()
        {
            var result = await _queue.GetAsync(Guid.NewGuid());

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetByStatusAsync_ReturnsMatchingJobs()
        {
            var pending1 = CreateDescriptor("Job1");
            var pending2 = CreateDescriptor("Job2");
            var completed = CreateDescriptor("Job3");
            completed.Status = JobStatus.Completed;

            await _queue.EnqueueAsync(pending1);
            await _queue.EnqueueAsync(pending2);
            await _queue.EnqueueAsync(completed);

            var results = await _queue.GetByStatusAsync(JobStatus.Pending);

            results.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetByStatusAsync_RespectsLimit()
        {
            for (int i = 0; i < 10; i++)
            {
                await _queue.EnqueueAsync(CreateDescriptor($"Job{i}"));
            }

            var results = await _queue.GetByStatusAsync(JobStatus.Pending, limit: 3);

            results.Should().HaveCount(3);
        }

        [Fact]
        public async Task PurgeAsync_RemovesOldCompletedJobs()
        {
            var old = CreateDescriptor("OldJob");
            old.Status = JobStatus.Completed;
            old.CompletedAt = DateTime.UtcNow.AddDays(-10);
            await _queue.EnqueueAsync(old);

            var recent = CreateDescriptor("RecentJob");
            recent.Status = JobStatus.Completed;
            recent.CompletedAt = DateTime.UtcNow;
            await _queue.EnqueueAsync(recent);

            var purged = await _queue.PurgeAsync(TimeSpan.FromDays(7));

            purged.Should().Be(1);
            (await _queue.GetAsync(old.Id)).Should().BeNull();
            (await _queue.GetAsync(recent.Id)).Should().NotBeNull();
        }

        [Fact]
        public async Task PurgeAsync_RemovesDeadAndCancelledJobs()
        {
            var dead = CreateDescriptor("Dead");
            dead.Status = JobStatus.Dead;
            dead.CompletedAt = DateTime.UtcNow.AddDays(-10);
            await _queue.EnqueueAsync(dead);

            var cancelled = CreateDescriptor("Cancelled");
            cancelled.Status = JobStatus.Cancelled;
            cancelled.CompletedAt = DateTime.UtcNow.AddDays(-10);
            await _queue.EnqueueAsync(cancelled);

            var purged = await _queue.PurgeAsync(TimeSpan.FromDays(7));

            purged.Should().Be(2);
        }

        [Fact]
        public async Task PurgeAsync_DoesNotRemovePendingJobs()
        {
            var pending = CreateDescriptor("Pending");
            await _queue.EnqueueAsync(pending);

            var purged = await _queue.PurgeAsync(TimeSpan.Zero);

            purged.Should().Be(0);
        }
    }
}
