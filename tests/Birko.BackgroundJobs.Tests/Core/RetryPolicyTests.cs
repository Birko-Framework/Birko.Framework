using System;
using FluentAssertions;
using Xunit;
using Birko;

namespace Birko.BackgroundJobs.Tests.Core
{
    public class RetryPolicyTests
    {
        [Fact]
        public void Default_HasExpectedValues()
        {
            var policy = RetryPolicy.Default;

            policy.MaxRetries.Should().Be(3);
            policy.BaseDelay.Should().Be(TimeSpan.FromSeconds(30));
            policy.MaxDelay.Should().Be(TimeSpan.FromHours(1));
            policy.UseExponentialBackoff.Should().BeTrue();
        }

        [Fact]
        public void None_HasZeroRetries()
        {
            var policy = RetryPolicy.None;

            policy.MaxRetries.Should().Be(0);
        }

        [Fact]
        public void GetDelay_ExponentialBackoff_DoublesEachAttempt()
        {
            var policy = new RetryPolicy
            {
                BaseDelay = TimeSpan.FromSeconds(10),
                MaxDelay = TimeSpan.FromHours(1),
                UseExponentialBackoff = true
            };

            policy.GetDelay(1).Should().Be(TimeSpan.FromSeconds(10));   // 10 * 2^0
            policy.GetDelay(2).Should().Be(TimeSpan.FromSeconds(20));   // 10 * 2^1
            policy.GetDelay(3).Should().Be(TimeSpan.FromSeconds(40));   // 10 * 2^2
            policy.GetDelay(4).Should().Be(TimeSpan.FromSeconds(80));   // 10 * 2^3
        }

        [Fact]
        public void GetDelay_ExponentialBackoff_CapsAtMaxDelay()
        {
            var policy = new RetryPolicy
            {
                BaseDelay = TimeSpan.FromMinutes(10),
                MaxDelay = TimeSpan.FromMinutes(30),
                UseExponentialBackoff = true
            };

            policy.GetDelay(1).Should().Be(TimeSpan.FromMinutes(10));   // 10 * 2^0
            policy.GetDelay(2).Should().Be(TimeSpan.FromMinutes(20));   // 10 * 2^1
            policy.GetDelay(3).Should().Be(TimeSpan.FromMinutes(30));   // 10 * 2^2 = 40, capped at 30
            policy.GetDelay(4).Should().Be(TimeSpan.FromMinutes(30));   // capped
        }

        [Fact]
        public void GetDelay_FixedDelay_ReturnsSameValue()
        {
            var policy = new RetryPolicy
            {
                BaseDelay = TimeSpan.FromSeconds(5),
                UseExponentialBackoff = false
            };

            policy.GetDelay(1).Should().Be(TimeSpan.FromSeconds(5));
            policy.GetDelay(2).Should().Be(TimeSpan.FromSeconds(5));
            policy.GetDelay(3).Should().Be(TimeSpan.FromSeconds(5));
            policy.GetDelay(10).Should().Be(TimeSpan.FromSeconds(5));
        }
    }
}
