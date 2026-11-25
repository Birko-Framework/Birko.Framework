using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Resilience.Configuration;
using Birko.AI.Resilience.Services;
using Birko.AI.Resilience.Stores;
using Birko.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Birko.AI.Resilience.Tests;

/// <summary>
/// TASK-984 (asked for by DraCode TASK-193): usage records, pricing and rate limits keyed on the client's display name,
/// so two providers of one type ("vLLM (finstat)" twice) recorded usage under one name, and a limit configured under
/// the consumer's own name ("finstat") never matched and was never applied. <see cref="TrackedLlmProvider.ProviderName"/>
/// is the configured name those use when set; without it nothing changes.
/// </summary>
public class TrackedLlmProviderNameTests
{
    private const string DisplayName = "vLLM (finstat)";

    private sealed class CapturingRepository : IUsageRepository
    {
        public readonly List<UsageRecordEntity> Records = new();
        public Task RecordUsageAsync(UsageRecordEntity entity, CancellationToken ct = default) { Records.Add(entity); return Task.CompletedTask; }
        public Task<double> GetTotalSpendAsync(DateTime from, DateTime to, string? provider = null, CancellationToken ct = default) => Task.FromResult(0d);
        public Task<double> GetProjectSpendAsync(string projectId, CancellationToken ct = default) => Task.FromResult(0d);
        public Task<List<ProviderUsageSummary>> GetUsageByProviderAsync(DateTime from, DateTime to, CancellationToken ct = default) => Task.FromResult(new List<ProviderUsageSummary>());
        public Task<ProjectUsageSummary?> GetUsageByProjectAsync(string projectId, DateTime from, DateTime to, CancellationToken ct = default) => Task.FromResult<ProjectUsageSummary?>(null);
    }

    /// <summary>A vLLM-like client: one display name whatever it is configured as.</summary>
    private sealed class VllmClient : ILlmProvider
    {
        public int Calls { get; private set; }
        public string Name => DisplayName;
        public Action<string, string>? MessageCallback { get; set; }
        public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new LlmResponse
            {
                StopReason = "end_turn",
                Content = [],
                Usage = new TokenUsage { PromptTokens = 1_000_000, CompletionTokens = 0, Model = "qwen" },
            });
        }
        public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static CostTrackingService Tracker(CapturingRepository repo) => new(new CostTrackingConfiguration
    {
        Enabled = true,
        Pricing =
        {
            new ProviderPricing { Provider = "finstat", Model = "*", InputPricePerMillionTokens = 2 },
            new ProviderPricing { Provider = "lan2", Model = "*", InputPricePerMillionTokens = 3 },
        }
    }, NullLogger<CostTrackingService>.Instance, repo);

    private static Task<LlmResponse> Send(ILlmProvider p) => p.SendMessageAsync(new List<Message>(), new List<Tool>(), "sys");

    [Fact]
    public async Task Usage_IsRecordedAndPriced_UnderEachConfiguredName()
    {
        var repo = new CapturingRepository();
        var tracker = Tracker(repo);
        var finstat = new TrackedLlmProvider(new VllmClient(), costTracker: tracker) { ProviderName = "finstat" };
        var lan2 = new TrackedLlmProvider(new VllmClient(), costTracker: tracker) { ProviderName = "lan2" };

        await Send(finstat);
        await Send(lan2);

        repo.Records.Select(r => (r.Provider, r.EstimatedCostUsd)).Should().Equal(("finstat", 2d), ("lan2", 3d));
    }

    [Fact]
    public async Task ARateLimit_UnderTheConfiguredName_HoldsThatProviderOnly()
    {
        var limiter = new ProviderRateLimiter(new RateLimitConfiguration
        {
            Enabled = true,
            ProviderLimits = { new ProviderRateLimit { Provider = "finstat", RequestsPerMinute = 1 } }
        });
        var finstatClient = new VllmClient();
        var lan2Client = new VllmClient();
        var finstat = new TrackedLlmProvider(finstatClient, rateLimiter: limiter)
        {
            ProviderName = "finstat", MaxRateLimitWaitAttempts = 2, MaxSingleRateLimitWait = TimeSpan.FromMilliseconds(5)
        };
        var lan2 = new TrackedLlmProvider(lan2Client, rateLimiter: limiter)
        {
            ProviderName = "lan2", MaxRateLimitWaitAttempts = 2, MaxSingleRateLimitWait = TimeSpan.FromMilliseconds(5)
        };

        (await Send(finstat)).StopReason.Should().Be("end_turn");
        (await Send(finstat)).StopReason.Should().Be("error", "the second call in the minute is over finstat's limit");
        (await Send(lan2)).StopReason.Should().Be("end_turn");
        (await Send(lan2)).StopReason.Should().Be("end_turn", "lan2 has no limit, and finstat's is not shared with it");

        finstatClient.Calls.Should().Be(1);
        lan2Client.Calls.Should().Be(2);
    }

    [Fact]
    public async Task WithoutAConfiguredName_TheDisplayNameIsUsed_AsBefore()
    {
        var repo = new CapturingRepository();
        var limiter = new ProviderRateLimiter(new RateLimitConfiguration
        {
            Enabled = true,
            ProviderLimits = { new ProviderRateLimit { Provider = DisplayName, RequestsPerMinute = 1 } }
        });
        var client = new VllmClient();
        var tracked = new TrackedLlmProvider(client, limiter, Tracker(repo))
        {
            MaxRateLimitWaitAttempts = 2, MaxSingleRateLimitWait = TimeSpan.FromMilliseconds(5)
        };

        tracked.ProviderName.Should().BeNull();
        await Send(tracked);
        (await Send(tracked)).StopReason.Should().Be("error");

        repo.Records.Should().ContainSingle().Which.Provider.Should().Be(DisplayName);
        client.Calls.Should().Be(1);
    }

    [Fact]
    public void Name_StaysTheClientsDisplayName()
    {
        var tracked = new TrackedLlmProvider(new VllmClient()) { ProviderName = "finstat" };

        tracked.Name.Should().Be(DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankConfiguredName_IsRefused(string blank)
    {
        var act = () => new TrackedLlmProvider(new VllmClient()) { ProviderName = blank };

        act.Should().Throw<ArgumentException>().WithMessage("*ProviderName*");
    }
}
