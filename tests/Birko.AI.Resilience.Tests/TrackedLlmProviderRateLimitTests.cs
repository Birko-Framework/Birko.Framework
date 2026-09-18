using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Resilience.Configuration;
using Birko.AI.Resilience.Services;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Resilience.Tests;

/// <summary>
/// Regression for CR-M011: when rate limited, TrackedLlmProvider delayed once and then called the
/// inner provider unconditionally — it never re-checked whether the window had actually cleared, so
/// the limiter never really prevented a call. It must now re-check and, if the limit still cannot be
/// satisfied within the attempt cap, return an error instead of proceeding.
/// </summary>
public class TrackedLlmProviderRateLimitTests
{
    private sealed class CountingProvider : ILlmProvider
    {
        public int Calls { get; private set; }
        public string Name => "anthropic";
        public Action<string, string>? MessageCallback { get; set; }
        public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new LlmResponse { StopReason = "end_turn", Content = [] });
        }
        public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task SendMessageAsync_ReturnsError_WhenRateLimitNeverClears()
    {
        var limiter = new ProviderRateLimiter(new RateLimitConfiguration
        {
            Enabled = true,
            ProviderLimits = { new ProviderRateLimit { Provider = "anthropic", RequestsPerMinute = 1 } }
        });
        limiter.RecordRequest("anthropic"); // fill the per-minute window (won't clear for ~60s)

        var inner = new CountingProvider();
        var tracked = new TrackedLlmProvider(inner, rateLimiter: limiter)
        {
            // Keep the test fast: two quick re-checks then give up.
            MaxRateLimitWaitAttempts = 2,
            MaxSingleRateLimitWait = TimeSpan.FromMilliseconds(5)
        };

        var response = await tracked.SendMessageAsync(new List<Message>(), new List<Tool>(), "sys");

        response.StopReason.Should().Be("error");
        inner.Calls.Should().Be(0, "the inner provider must not be called when the limit cannot be satisfied");
    }

    [Fact]
    public async Task SendMessageAsync_CallsInner_WhenNotRateLimited()
    {
        var limiter = new ProviderRateLimiter(new RateLimitConfiguration
        {
            Enabled = true,
            ProviderLimits = { new ProviderRateLimit { Provider = "anthropic", RequestsPerMinute = 10 } }
        });

        var inner = new CountingProvider();
        var tracked = new TrackedLlmProvider(inner, rateLimiter: limiter);

        var response = await tracked.SendMessageAsync(new List<Message>(), new List<Tool>(), "sys");

        response.StopReason.Should().Be("end_turn");
        inner.Calls.Should().Be(1);
    }
}
