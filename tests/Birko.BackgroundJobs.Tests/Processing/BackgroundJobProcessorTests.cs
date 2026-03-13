using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.BackgroundJobs.Serialization;

namespace Birko.BackgroundJobs.Tests.Processing
{
    public class BackgroundJobProcessorTests
    {
        private readonly InMemoryJobQueue _queue = new();
        private readonly JobExecutor _executor;

        public BackgroundJobProcessorTests()
        {
            _executor = new JobExecutor(type => Activator.CreateInstance(type)!, new JsonJobSerializer());
        }

        [Fact]
        public async Task RunAsync_ProcessesEnqueuedJob()
        {
            var descriptor = new JobDescriptor
            {
                JobType = typeof(SuccessJob).AssemblyQualifiedName!
            };
            await _queue.EnqueueAsync(descriptor);

            var processor = new BackgroundJobProcessor(_queue, _executor, new JobQueueOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50),
                MaxConcurrency = 1
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await processor.RunAsync(cts.Token);

            var job = await _queue.GetAsync(descriptor.Id);
            job.Should().NotBeNull();
            job!.Status.Should().Be(JobStatus.Completed);
        }

        [Fact]
        public async Task RunAsync_FailedJob_GetsRetried()
        {
            var descriptor = new JobDescriptor
            {
                JobType = typeof(FailingJob).AssemblyQualifiedName!,
                MaxRetries = 3
            };
            await _queue.EnqueueAsync(descriptor);

            var processor = new BackgroundJobProcessor(_queue, _executor, new JobQueueOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50),
                MaxConcurrency = 1
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await processor.RunAsync(cts.Token);

            var job = await _queue.GetAsync(descriptor.Id);
            job.Should().NotBeNull();
            // After first attempt it should be scheduled for retry (status = Scheduled)
            // or if enough time passed, it could have been retried again
            job!.LastError.Should().Contain("Job failed intentionally");
            job.AttemptCount.Should().BeGreaterThanOrEqualTo(1);
        }

        [Fact]
        public async Task RunAsync_StopsOnCancellation()
        {
            var processor = new BackgroundJobProcessor(_queue, _executor, new JobQueueOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50)
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            // Should not throw, should complete gracefully
            await processor.RunAsync(cts.Token);
        }

        [Fact]
        public async Task Stop_CancelsProcessing()
        {
            var processor = new BackgroundJobProcessor(_queue, _executor, new JobQueueOptions
            {
                PollingInterval = TimeSpan.FromMilliseconds(50)
            });

            var task = Task.Run(async () =>
            {
                await processor.RunAsync();
            });

            await Task.Delay(200);
            processor.Stop();

            // Should complete within a reasonable time
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
            completed.Should().BeSameAs(task);
        }

        [Fact]
        public void Constructor_NullQueue_Throws()
        {
            var act = () => new BackgroundJobProcessor(null!, _executor);

            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_NullExecutor_Throws()
        {
            var act = () => new BackgroundJobProcessor(_queue, null!);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
