using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;

namespace Birko.BackgroundJobs.Tests.Processing
{
    public class RecurringJobSchedulerTests
    {
        private readonly InMemoryJobQueue _queue = new();

        [Fact]
        public void Register_AddsJob()
        {
            var scheduler = new RecurringJobScheduler(_queue);

            // Should not throw
            scheduler.Register<SuccessJob>("cleanup", TimeSpan.FromMinutes(5));
        }

        [Fact]
        public void Remove_RegisteredJob_ReturnsTrue()
        {
            var scheduler = new RecurringJobScheduler(_queue);
            scheduler.Register<SuccessJob>("cleanup", TimeSpan.FromMinutes(5));

            var result = scheduler.Remove("cleanup");

            result.Should().BeTrue();
        }

        [Fact]
        public void Remove_UnregisteredJob_ReturnsFalse()
        {
            var scheduler = new RecurringJobScheduler(_queue);

            var result = scheduler.Remove("nonexistent");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task RunAsync_EnqueuesJobWhenIntervalElapses()
        {
            var scheduler = new RecurringJobScheduler(_queue);
            // Register with a very short interval so it fires quickly
            scheduler.Register<SuccessJob>("fast-job", TimeSpan.FromMilliseconds(100));

            // Manually set NextRunAt to the past so it fires immediately
            // We can't directly access the internal state, but we can register
            // with a tiny interval and wait for it
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var task = scheduler.RunAsync(cts.Token);

            // Wait for at least one firing
            await Task.Delay(2000);
            cts.Cancel();

            try { await task; } catch (OperationCanceledException) { }

            var pending = await _queue.GetByStatusAsync(JobStatus.Pending);
            pending.Should().HaveCountGreaterThan(0);

            // Verify the enqueued job has the recurring metadata
            pending[0].Metadata.Should().ContainKey("recurring.name");
            pending[0].Metadata["recurring.name"].Should().Be("fast-job");
        }

        [Fact]
        public async Task RunAsync_StopsOnCancellation()
        {
            var scheduler = new RecurringJobScheduler(_queue);
            scheduler.Register<SuccessJob>("test", TimeSpan.FromHours(1));

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            // Should complete without throwing
            await scheduler.RunAsync(cts.Token);
        }

        [Fact]
        public async Task Register_WithQueueName_SetsQueueOnEnqueuedJob()
        {
            var scheduler = new RecurringJobScheduler(_queue);
            scheduler.Register<SuccessJob>("queued-job", TimeSpan.FromMilliseconds(100), "maintenance");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var task = scheduler.RunAsync(cts.Token);
            await Task.Delay(2000);
            cts.Cancel();
            try { await task; } catch (OperationCanceledException) { }

            var pending = await _queue.GetByStatusAsync(JobStatus.Pending);
            pending.Should().HaveCountGreaterThan(0);
            pending[0].QueueName.Should().Be("maintenance");
        }

        [Fact]
        public void Constructor_NullQueue_Throws()
        {
            var act = () => new RecurringJobScheduler(null!);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
