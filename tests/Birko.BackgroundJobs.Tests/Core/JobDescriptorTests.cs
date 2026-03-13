using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using Birko.BackgroundJobs;

namespace Birko.BackgroundJobs.Tests.Core
{
    public class JobDescriptorTests
    {
        [Fact]
        public void NewDescriptor_HasDefaults()
        {
            var descriptor = new JobDescriptor();

            descriptor.Id.Should().NotBe(Guid.Empty);
            descriptor.JobType.Should().BeEmpty();
            descriptor.SerializedInput.Should().BeNull();
            descriptor.InputType.Should().BeNull();
            descriptor.QueueName.Should().BeNull();
            descriptor.Priority.Should().Be(0);
            descriptor.MaxRetries.Should().Be(3);
            descriptor.Delay.Should().BeNull();
            descriptor.ScheduledAt.Should().BeNull();
            descriptor.Status.Should().Be(JobStatus.Pending);
            descriptor.AttemptCount.Should().Be(0);
            descriptor.LastAttemptAt.Should().BeNull();
            descriptor.CompletedAt.Should().BeNull();
            descriptor.LastError.Should().BeNull();
            descriptor.Metadata.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void NewDescriptor_EnqueuedAtIsUtcNow()
        {
            var before = DateTime.UtcNow;
            var descriptor = new JobDescriptor();
            var after = DateTime.UtcNow;

            descriptor.EnqueuedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        }

        [Fact]
        public void TwoDescriptors_HaveDifferentIds()
        {
            var d1 = new JobDescriptor();
            var d2 = new JobDescriptor();

            d1.Id.Should().NotBe(d2.Id);
        }

        [Fact]
        public void Metadata_CanBePopulated()
        {
            var descriptor = new JobDescriptor
            {
                Metadata = new Dictionary<string, string>
                {
                    ["correlation-id"] = "abc-123",
                    ["user"] = "admin"
                }
            };

            descriptor.Metadata.Should().HaveCount(2);
            descriptor.Metadata["correlation-id"].Should().Be("abc-123");
        }

        [Fact]
        public void AllProperties_CanBeSet()
        {
            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var descriptor = new JobDescriptor
            {
                Id = id,
                JobType = "MyApp.Jobs.CleanupJob, MyApp",
                SerializedInput = "{\"days\":30}",
                InputType = "MyApp.Jobs.CleanupInput, MyApp",
                QueueName = "maintenance",
                Priority = 5,
                MaxRetries = 5,
                Delay = TimeSpan.FromMinutes(10),
                ScheduledAt = now.AddMinutes(10),
                Status = JobStatus.Scheduled,
                AttemptCount = 2,
                LastAttemptAt = now,
                CompletedAt = now,
                LastError = "Timeout"
            };

            descriptor.Id.Should().Be(id);
            descriptor.JobType.Should().Be("MyApp.Jobs.CleanupJob, MyApp");
            descriptor.Priority.Should().Be(5);
            descriptor.Status.Should().Be(JobStatus.Scheduled);
            descriptor.AttemptCount.Should().Be(2);
            descriptor.LastError.Should().Be("Timeout");
        }
    }
}
