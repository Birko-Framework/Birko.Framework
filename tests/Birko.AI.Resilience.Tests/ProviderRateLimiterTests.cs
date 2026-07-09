using Birko.AI.Resilience.Configuration;
using Birko.AI.Resilience.Services;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Resilience.Tests;

/// <summary>
/// Regression for CR-M012: GetRetryAfter only considered RequestsPerMinute and returned null when a
/// request was blocked by TokensPerMinute, RequestsPerDay or TokensPerDay — so a blocked caller got
/// no usable backoff. It must now return a positive delay for every kind of active blocking window.
/// </summary>
public class ProviderRateLimiterTests
{
    private static ProviderRateLimiter Build(ProviderRateLimit limit)
        => new(new RateLimitConfiguration { Enabled = true, ProviderLimits = { limit } });

    [Fact]
    public void GetRetryAfter_ReturnsDelay_WhenBlockedByTokensPerMinute()
    {
        var limiter = Build(new ProviderRateLimit { Provider = "p", RequestsPerMinute = 0, TokensPerMinute = 100 });
        limiter.RecordRequest("p", tokenCount: 100);

        limiter.CanMakeRequest("p").Should().BeFalse();
        var retryAfter = limiter.GetRetryAfter("p");

        retryAfter.Should().NotBeNull();
        retryAfter!.Value.Should().BeGreaterThan(TimeSpan.Zero);
        retryAfter.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GetRetryAfter_ReturnsDelay_WhenBlockedByRequestsPerDay()
    {
        var limiter = Build(new ProviderRateLimit { Provider = "p", RequestsPerMinute = 0, RequestsPerDay = 1 });
        limiter.RecordRequest("p");

        limiter.CanMakeRequest("p").Should().BeFalse();
        var retryAfter = limiter.GetRetryAfter("p");

        retryAfter.Should().NotBeNull();
        retryAfter!.Value.Should().BeGreaterThan(TimeSpan.Zero);
        retryAfter.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromDays(1));
    }

    [Fact]
    public void GetRetryAfter_ReturnsMax_WhenBlockedByBothMinuteAndDay()
    {
        var limiter = Build(new ProviderRateLimit { Provider = "p", RequestsPerMinute = 1, RequestsPerDay = 1 });
        limiter.RecordRequest("p");

        // Both windows are full; the daily window (up to ~24h) dominates the minute window.
        var retryAfter = limiter.GetRetryAfter("p");
        retryAfter.Should().NotBeNull();
        retryAfter!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GetRetryAfter_ReturnsNull_WhenNotBlocked()
    {
        var limiter = Build(new ProviderRateLimit { Provider = "p", RequestsPerMinute = 10 });
        limiter.RecordRequest("p");

        limiter.CanMakeRequest("p").Should().BeTrue();
        limiter.GetRetryAfter("p").Should().BeNull();
    }
}
