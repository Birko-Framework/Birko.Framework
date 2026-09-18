using System;
using Birko.MessageQueue.Retry;
using FluentAssertions;
using Xunit;

namespace Birko.MessageQueue.Tests.Core
{
    /// <summary>
    /// CR-M199: RetryPolicy.GetDelay (the only branch logic in the core project) had no tests, and its
    /// exponential path had the CR-M078-style overflow — `(long)Math.Pow(2, n-1)` wrapped negative for
    /// large attempt numbers, producing a negative TimeSpan that slipped past the MaxDelay clamp.
    /// </summary>
    public class RetryPolicyTests
    {
        [Fact]
        public void FixedBackoff_ReturnsBaseDelay()
        {
            var policy = new RetryPolicy { UseExponentialBackoff = false, BaseDelay = TimeSpan.FromSeconds(2) };
            policy.GetDelay(1).Should().Be(TimeSpan.FromSeconds(2));
            policy.GetDelay(10).Should().Be(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void ExponentialBackoff_GrowsByPowerOfTwo()
        {
            var policy = new RetryPolicy
            {
                UseExponentialBackoff = true,
                BaseDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromHours(1),
            };
            policy.GetDelay(1).Should().Be(TimeSpan.FromSeconds(1)); // 2^0
            policy.GetDelay(2).Should().Be(TimeSpan.FromSeconds(2)); // 2^1
            policy.GetDelay(3).Should().Be(TimeSpan.FromSeconds(4)); // 2^2
            policy.GetDelay(4).Should().Be(TimeSpan.FromSeconds(8)); // 2^3
        }

        [Fact]
        public void ExponentialBackoff_ClampsAtMaxDelay()
        {
            var policy = new RetryPolicy
            {
                UseExponentialBackoff = true,
                BaseDelay = TimeSpan.FromSeconds(5),
                MaxDelay = TimeSpan.FromMinutes(1),
            };
            policy.GetDelay(20).Should().Be(TimeSpan.FromMinutes(1));
        }

        [Theory]
        [InlineData(53)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(int.MaxValue)]
        public void ExponentialBackoff_LargeAttempt_NeverNegative_SaturatesAtMax(int attempt)
        {
            var policy = RetryPolicy.Default; // 5s base, 5min max, exponential
            var delay = policy.GetDelay(attempt);
            delay.Should().BeGreaterThan(TimeSpan.Zero, "an overflowed cast must not yield a negative delay (CR-M199)");
            delay.Should().Be(policy.MaxDelay);
        }

        [Fact]
        public void Factories_HaveExpectedDefaults()
        {
            RetryPolicy.Default.MaxRetries.Should().Be(3);
            RetryPolicy.Default.UseExponentialBackoff.Should().BeTrue();
            RetryPolicy.None.MaxRetries.Should().Be(0);
        }
    }
}
