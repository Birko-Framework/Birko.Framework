using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using Birko.BackgroundJobs;

namespace Birko.BackgroundJobs.Tests.Core
{
    public class JobContextTests
    {
        [Fact]
        public void Constructor_SetsAllProperties()
        {
            var id = Guid.NewGuid();
            var enqueuedAt = DateTime.UtcNow;
            var metadata = new Dictionary<string, string> { ["key"] = "value" };

            var context = new JobContext(id, 3, enqueuedAt, metadata);

            context.JobId.Should().Be(id);
            context.AttemptNumber.Should().Be(3);
            context.EnqueuedAt.Should().Be(enqueuedAt);
            context.Metadata.Should().ContainKey("key").WhoseValue.Should().Be("value");
        }

        [Fact]
        public void Constructor_NullMetadata_DefaultsToEmpty()
        {
            var context = new JobContext(Guid.NewGuid(), 1, DateTime.UtcNow);

            context.Metadata.Should().NotBeNull().And.BeEmpty();
        }
    }
}
