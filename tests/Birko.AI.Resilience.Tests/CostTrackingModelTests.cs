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
/// Regression for CR-H007: TrackedLlmProvider recorded Model: "" for every call, so
/// CostTrackingService could never resolve a per-model price ("provider:" never matches a
/// configured "provider:model" entry) — every consumer with distinct per-model prices silently
/// got the wildcard price or $0, and persisted usage had no model breakdown. TokenUsage now
/// carries a Model, populated by the providers and passed through here.
/// </summary>
public class CostTrackingModelTests
{
    private sealed class CapturingRepository : IUsageRepository
    {
        public readonly List<UsageRecordEntity> Records = new();
        public Task RecordUsageAsync(UsageRecordEntity entity) { Records.Add(entity); return Task.CompletedTask; }
        public Task<double> GetTotalSpendAsync(DateTime from, DateTime to) => Task.FromResult(0d);
        public Task<double> GetProjectSpendAsync(string projectId) => Task.FromResult(0d);
        public Task<List<ProviderUsageSummary>> GetUsageByProviderAsync(DateTime from, DateTime to) => Task.FromResult(new List<ProviderUsageSummary>());
        public Task<ProjectUsageSummary?> GetUsageByProjectAsync(string projectId, DateTime from, DateTime to) => Task.FromResult<ProjectUsageSummary?>(null);
    }

    private sealed class FakeProvider : ILlmProvider
    {
        private readonly LlmResponse _response;
        public FakeProvider(LlmResponse response) => _response = response;
        public string Name => "anthropic";
        public Action<string, string>? MessageCallback { get; set; }
        public Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default) => Task.FromResult(_response);
        public Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static CostTrackingConfiguration ConfigWithPerModelPricing() => new()
    {
        Enabled = true,
        Pricing =
        {
            new ProviderPricing { Provider = "anthropic", Model = "*", InputPricePerMillionTokens = 1, OutputPricePerMillionTokens = 1 },
            new ProviderPricing { Provider = "anthropic", Model = "claude-opus", InputPricePerMillionTokens = 15, OutputPricePerMillionTokens = 75 },
        }
    };

    [Fact]
    public void CalculateCost_UsesPerModelPricing_WhenModelSupplied()
    {
        var svc = new CostTrackingService(ConfigWithPerModelPricing(), NullLogger<CostTrackingService>.Instance);

        var opusCost = svc.CalculateCost("anthropic", "claude-opus", 1_000_000, 0);
        var wildcardCost = svc.CalculateCost("anthropic", "", 1_000_000, 0);

        opusCost.Should().Be(15);      // per-model input price
        wildcardCost.Should().Be(1);   // falls back to wildcard
        opusCost.Should().NotBe(wildcardCost);
    }

    [Fact]
    public async Task TrackedLlmProvider_ForwardsResponseModelToUsageRecord()
    {
        var repo = new CapturingRepository();
        var tracker = new CostTrackingService(ConfigWithPerModelPricing(), NullLogger<CostTrackingService>.Instance, repo);
        var inner = new FakeProvider(new LlmResponse
        {
            Content = new(),
            Usage = new TokenUsage { PromptTokens = 1_000_000, CompletionTokens = 0, Model = "claude-opus" }
        });

        var tracked = new TrackedLlmProvider(inner, rateLimiter: null, costTracker: tracker);
        await tracked.SendMessageAsync(new List<Message>(), new List<Tool>(), "sys");

        repo.Records.Should().ContainSingle();
        repo.Records[0].Model.Should().Be("claude-opus");
        repo.Records[0].EstimatedCostUsd.Should().Be(15); // per-model price, not the $1 wildcard
    }
}
