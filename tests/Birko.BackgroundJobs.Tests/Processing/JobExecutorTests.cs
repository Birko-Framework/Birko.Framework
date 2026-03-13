using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.Processing;
using Birko.BackgroundJobs.Serialization;

namespace Birko.BackgroundJobs.Tests.Processing
{
    public class JobExecutorTests
    {
        private readonly JsonJobSerializer _serializer = new();

        private JobExecutor CreateExecutor(Func<Type, object>? factory = null)
        {
            return new JobExecutor(
                factory ?? (type => Activator.CreateInstance(type)!),
                _serializer);
        }

        [Fact]
        public async Task ExecuteAsync_ParameterlessJob_Succeeds()
        {
            var executor = CreateExecutor();
            var descriptor = new JobDescriptor
            {
                JobType = typeof(SuccessJob).AssemblyQualifiedName!,
                AttemptCount = 1
            };

            var result = await executor.ExecuteAsync(descriptor);

            result.Success.Should().BeTrue();
            result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
            result.Error.Should().BeNull();
        }

        [Fact]
        public async Task ExecuteAsync_JobWithInput_DeserializesAndExecutes()
        {
            var executor = CreateExecutor();
            var input = new EmailInput { To = "user@example.com", Subject = "Test", Body = "Content" };
            var descriptor = new JobDescriptor
            {
                JobType = typeof(SendEmailJob).AssemblyQualifiedName!,
                InputType = typeof(EmailInput).AssemblyQualifiedName!,
                SerializedInput = _serializer.Serialize(input),
                AttemptCount = 1
            };

            var result = await executor.ExecuteAsync(descriptor);

            result.Success.Should().BeTrue();
            SendEmailJob.LastInput.Should().NotBeNull();
            SendEmailJob.LastInput!.To.Should().Be("user@example.com");
        }

        [Fact]
        public async Task ExecuteAsync_FailingJob_ReturnsFailure()
        {
            var executor = CreateExecutor();
            var descriptor = new JobDescriptor
            {
                JobType = typeof(FailingJob).AssemblyQualifiedName!,
                AttemptCount = 1
            };

            var result = await executor.ExecuteAsync(descriptor);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Job failed intentionally");
            result.Exception.Should().NotBeNull();
        }

        [Fact]
        public async Task ExecuteAsync_UnknownJobType_ReturnsFailure()
        {
            var executor = CreateExecutor();
            var descriptor = new JobDescriptor
            {
                JobType = "NonExistent.Job, NonExistent"
            };

            var result = await executor.ExecuteAsync(descriptor);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Job type not found");
        }

        [Fact]
        public async Task ExecuteAsync_UnknownInputType_ReturnsFailure()
        {
            var executor = CreateExecutor();
            var descriptor = new JobDescriptor
            {
                JobType = typeof(SendEmailJob).AssemblyQualifiedName!,
                InputType = "NonExistent.Input, NonExistent",
                SerializedInput = "{}"
            };

            var result = await executor.ExecuteAsync(descriptor);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Input type not found");
        }

        [Fact]
        public async Task ExecuteAsync_PassesContextToJob()
        {
            var executor = CreateExecutor();
            var descriptor = new JobDescriptor
            {
                JobType = typeof(ContextCapturingJob).AssemblyQualifiedName!,
                AttemptCount = 2,
                Metadata = { ["trace-id"] = "abc123" }
            };

            await executor.ExecuteAsync(descriptor);

            ContextCapturingJob.LastContext.Should().NotBeNull();
            ContextCapturingJob.LastContext!.JobId.Should().Be(descriptor.Id);
            ContextCapturingJob.LastContext.AttemptNumber.Should().Be(2);
            ContextCapturingJob.LastContext.Metadata.Should().ContainKey("trace-id");
        }

        [Fact]
        public void Constructor_NullFactory_Throws()
        {
            var act = () => new JobExecutor(null!);

            act.Should().Throw<ArgumentNullException>();
        }
    }
}
