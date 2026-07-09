using System;
using Birko;
using FluentAssertions;
using Xunit;

namespace Birko.Contracts.Tests
{
    /// <summary>
    /// Tests for <see cref="RetryPolicy.GetDelay"/> — fixed vs exponential delay, MaxDelay
    /// saturation (incl. the high-attempt overflow that used to produce a negative TimeSpan,
    /// CR-M078), and the +-25% jitter window.
    /// </summary>
    public class RetryPolicyTests
    {
        [Fact]
        public void Default_policy_has_expected_values()
        {
            var policy = RetryPolicy.Default;

            policy.MaxRetries.Should().Be(3);
            policy.BaseDelay.Should().Be(TimeSpan.FromSeconds(5));
            policy.MaxDelay.Should().Be(TimeSpan.FromMinutes(5));
            policy.UseExponentialBackoff.Should().BeTrue();
            policy.BackoffMultiplier.Should().Be(2.0);
            policy.AddJitter.Should().BeFalse();
        }

        [Fact]
        public void None_policy_has_no_retries()
        {
            RetryPolicy.None.MaxRetries.Should().Be(0);
        }

        [Fact]
        public void GetDelay_returns_fixed_base_delay_when_exponential_disabled()
        {
            var policy = new RetryPolicy
            {
                UseExponentialBackoff = false,
                BaseDelay = TimeSpan.FromSeconds(3),
            };

            policy.GetDelay(1).Should().Be(TimeSpan.FromSeconds(3));
            policy.GetDelay(10).Should().Be(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void GetDelay_grows_exponentially()
        {
            var policy = new RetryPolicy
            {
                UseExponentialBackoff = true,
                BaseDelay = TimeSpan.FromSeconds(1),
                BackoffMultiplier = 2.0,
                MaxDelay = TimeSpan.FromHours(1),
            };

            policy.GetDelay(1).Should().Be(TimeSpan.FromSeconds(1)); // 1 * 2^0
            policy.GetDelay(2).Should().Be(TimeSpan.FromSeconds(2)); // 1 * 2^1
            policy.GetDelay(3).Should().Be(TimeSpan.FromSeconds(4)); // 1 * 2^2
            policy.GetDelay(4).Should().Be(TimeSpan.FromSeconds(8)); // 1 * 2^3
        }

        [Fact]
        public void GetDelay_saturates_at_MaxDelay()
        {
            var policy = new RetryPolicy
            {
                UseExponentialBackoff = true,
                BaseDelay = TimeSpan.FromSeconds(5),
                BackoffMultiplier = 2.0,
                MaxDelay = TimeSpan.FromMinutes(5),
            };

            // 5s * 2^9 = 2560s > 300s cap.
            policy.GetDelay(10).Should().Be(TimeSpan.FromMinutes(5));
        }

        [Theory]
        [InlineData(53)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(int.MaxValue)]
        public void GetDelay_never_returns_a_negative_delay_at_high_attempt_numbers(int attempt)
        {
            // CR-M078: (long)Math.Pow(2.0, attempt-1) overflowed to long.MinValue around attempt 53+,
            // producing a negative TimeSpan that slipped past the '> MaxDelay' clamp. The value must
            // saturate at MaxDelay, and must never be negative (Task.Delay would throw).
            var policy = RetryPolicy.Default; // BaseDelay=5s, mult=2, MaxDelay=5min

            var delay = policy.GetDelay(attempt);

            delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
            delay.Should().Be(policy.MaxDelay);
        }

        [Fact]
        public void GetDelay_applies_jitter_within_plus_minus_25_percent()
        {
            var policy = new RetryPolicy
            {
                UseExponentialBackoff = false,
                BaseDelay = TimeSpan.FromSeconds(100),
                AddJitter = true,
            };

            var lower = TimeSpan.FromSeconds(75);  // 0.75x
            var upper = TimeSpan.FromSeconds(125); // 1.25x

            for (var i = 0; i < 200; i++)
            {
                var delay = policy.GetDelay(1);
                delay.Should().BeGreaterThanOrEqualTo(lower);
                delay.Should().BeLessThanOrEqualTo(upper);
            }
        }
    }
}
