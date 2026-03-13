using System;
using FluentAssertions;
using Xunit;
using Birko.BackgroundJobs;

namespace Birko.BackgroundJobs.Tests.Core
{
    public class JobResultTests
    {
        [Fact]
        public void Succeeded_ReturnsSuccessResult()
        {
            var duration = TimeSpan.FromMilliseconds(150);

            var result = JobResult.Succeeded(duration);

            result.Success.Should().BeTrue();
            result.Duration.Should().Be(duration);
            result.Error.Should().BeNull();
            result.Exception.Should().BeNull();
        }

        [Fact]
        public void Failed_ReturnsFailureWithError()
        {
            var duration = TimeSpan.FromMilliseconds(50);

            var result = JobResult.Failed(duration, "Something went wrong");

            result.Success.Should().BeFalse();
            result.Duration.Should().Be(duration);
            result.Error.Should().Be("Something went wrong");
            result.Exception.Should().BeNull();
        }

        [Fact]
        public void Failed_ReturnsFailureWithException()
        {
            var duration = TimeSpan.FromMilliseconds(75);
            var ex = new InvalidOperationException("Bad state");

            var result = JobResult.Failed(duration, ex.Message, ex);

            result.Success.Should().BeFalse();
            result.Error.Should().Be("Bad state");
            result.Exception.Should().BeSameAs(ex);
        }
    }
}
