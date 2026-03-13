using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;

namespace Birko.BackgroundJobs.Tests.Processing
{
    public class JobDispatcherTests
    {
        private readonly InMemoryJobQueue _queue = new();
        private readonly JobDispatcher _dispatcher;

        public JobDispatcherTests()
        {
            _dispatcher = new JobDispatcher(_queue);
        }

        [Fact]
        public async Task EnqueueAsync_ParameterlessJob_EnqueuesWithCorrectType()
        {
            var id = await _dispatcher.EnqueueAsync<SuccessJob>();

            id.Should().NotBe(Guid.Empty);
            var job = await _queue.GetAsync(id);
            job.Should().NotBeNull();
            job!.JobType.Should().Contain(nameof(SuccessJob));
            job.Status.Should().Be(JobStatus.Pending);
            job.SerializedInput.Should().BeNull();
        }

        [Fact]
        public async Task EnqueueAsync_WithInput_SerializesInput()
        {
            var input = new EmailInput { To = "test@example.com", Subject = "Hi", Body = "Hello" };

            var id = await _dispatcher.EnqueueAsync<SendEmailJob, EmailInput>(input);

            var job = await _queue.GetAsync(id);
            job.Should().NotBeNull();
            job!.SerializedInput.Should().NotBeNullOrEmpty();
            job.InputType.Should().Contain(nameof(EmailInput));
        }

        [Fact]
        public async Task ScheduleAsync_SetsDelayAndScheduledStatus()
        {
            var delay = TimeSpan.FromMinutes(5);
            var before = DateTime.UtcNow;

            var id = await _dispatcher.ScheduleAsync<SuccessJob>(delay);

            var job = await _queue.GetAsync(id);
            job.Should().NotBeNull();
            job!.Status.Should().Be(JobStatus.Scheduled);
            job.Delay.Should().Be(delay);
            job.ScheduledAt.Should().NotBeNull();
            job.ScheduledAt!.Value.Should().BeOnOrAfter(before.Add(delay));
        }

        [Fact]
        public async Task ScheduleAsync_WithInput_SerializesAndSchedules()
        {
            var input = new EmailInput { To = "a@b.com", Subject = "Delayed", Body = "Later" };
            var delay = TimeSpan.FromHours(1);

            var id = await _dispatcher.ScheduleAsync<SendEmailJob, EmailInput>(input, delay);

            var job = await _queue.GetAsync(id);
            job.Should().NotBeNull();
            job!.Status.Should().Be(JobStatus.Scheduled);
            job.SerializedInput.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task EnqueueOnAsync_SetsQueueName()
        {
            var id = await _dispatcher.EnqueueOnAsync<SuccessJob>("critical");

            var job = await _queue.GetAsync(id);
            job.Should().NotBeNull();
            job!.QueueName.Should().Be("critical");
        }

        [Fact]
        public async Task EnqueueWithPriorityAsync_SetsPriority()
        {
            var id = await _dispatcher.EnqueueWithPriorityAsync<SuccessJob>(10);

            var job = await _queue.GetAsync(id);
            job.Should().NotBeNull();
            job!.Priority.Should().Be(10);
        }

        [Fact]
        public async Task CancelAsync_PendingJob_ReturnsTrue()
        {
            var id = await _dispatcher.EnqueueAsync<SuccessJob>();

            var result = await _dispatcher.CancelAsync(id);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task GetStatusAsync_ReturnsCurrentStatus()
        {
            var id = await _dispatcher.EnqueueAsync<SuccessJob>();

            var status = await _dispatcher.GetStatusAsync(id);

            status.Should().Be(JobStatus.Pending);
        }

        [Fact]
        public async Task GetStatusAsync_NonexistentJob_ReturnsNull()
        {
            var status = await _dispatcher.GetStatusAsync(Guid.NewGuid());

            status.Should().BeNull();
        }

        [Fact]
        public void Constructor_NullQueue_Throws()
        {
            var act = () => new JobDispatcher(null!);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
