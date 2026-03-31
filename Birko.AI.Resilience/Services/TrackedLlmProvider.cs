using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using Microsoft.Extensions.Logging;

namespace Birko.AI.Resilience.Services
{
    /// <summary>
    /// Decorator that wraps an ILlmProvider with rate limiting and cost tracking.
    /// </summary>
    public class TrackedLlmProvider : ILlmProvider
    {
        private readonly ILlmProvider _inner;
        private readonly ProviderRateLimiter? _rateLimiter;
        private readonly CostTrackingService? _costTracker;
        private readonly ILogger? _logger;

        public string? ProjectId { get; set; }
        public string? TaskId { get; set; }
        public string? AgentType { get; set; }
        public string? CallerContext { get; set; }

        public string Name => _inner.Name;

        public Action<string, string>? MessageCallback
        {
            get => _inner.MessageCallback;
            set => _inner.MessageCallback = value;
        }

        public TrackedLlmProvider(
            ILlmProvider inner,
            ProviderRateLimiter? rateLimiter = null,
            CostTrackingService? costTracker = null,
            ILogger? logger = null)
        {
            _inner = inner;
            _rateLimiter = rateLimiter;
            _costTracker = costTracker;
            _logger = logger;
        }

        public async Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            if (_rateLimiter != null && !_rateLimiter.CanMakeRequest(Name))
            {
                var retryAfter = _rateLimiter.GetRetryAfter(Name);
                if (retryAfter.HasValue && retryAfter.Value.TotalSeconds > 0)
                {
                    _logger?.LogWarning("Rate limited for {Provider}, waiting {Seconds:F1}s", Name, retryAfter.Value.TotalSeconds);
                    await Task.Delay(retryAfter.Value);
                }
                else
                {
                    await Task.Delay(1000);
                }
            }

            if (_costTracker != null)
            {
                var budgetStatus = await _costTracker.CheckBudgetAsync(ProjectId);
                if (!budgetStatus.IsWithinBudget)
                {
                    var msg = $"Budget exceeded ({budgetStatus.BudgetType}): ${budgetStatus.CurrentSpend:F2} / ${budgetStatus.BudgetLimit:F2}";
                    _logger?.LogWarning(msg);
                    return LlmResponse.Error(msg);
                }
            }

            var response = await _inner.SendMessageAsync(messages, tools, systemPrompt);
            await RecordUsageFromResponse(response.Usage);
            return response;
        }

        public async Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt)
        {
            if (_rateLimiter != null && !_rateLimiter.CanMakeRequest(Name))
            {
                var retryAfter = _rateLimiter.GetRetryAfter(Name);
                if (retryAfter.HasValue && retryAfter.Value.TotalSeconds > 0)
                {
                    _logger?.LogWarning("Rate limited for {Provider} (streaming), waiting {Seconds:F1}s", Name, retryAfter.Value.TotalSeconds);
                    await Task.Delay(retryAfter.Value);
                }
                else
                {
                    await Task.Delay(1000);
                }
            }

            if (_costTracker != null)
            {
                var budgetStatus = await _costTracker.CheckBudgetAsync(ProjectId);
                if (!budgetStatus.IsWithinBudget)
                {
                    var msg = $"Budget exceeded ({budgetStatus.BudgetType}): ${budgetStatus.CurrentSpend:F2} / ${budgetStatus.BudgetLimit:F2}";
                    return new LlmStreamingResponse
                    {
                        GetStreamAsync = () => Task.FromResult<IAsyncEnumerable<string>>(EmptyStream()),
                        Error = msg,
                        FinalResponse = LlmResponse.Error(msg)
                    };
                }
            }

            var streamingResponse = await _inner.SendMessageStreamingAsync(messages, tools, systemPrompt);

            var originalGetStream = streamingResponse.GetStreamAsync;
            streamingResponse.GetStreamAsync = async () =>
            {
                var stream = await originalGetStream();
                return WrapStreamForUsageTracking(stream, streamingResponse);
            };

            return streamingResponse;
        }

        private async IAsyncEnumerable<string> WrapStreamForUsageTracking(
            IAsyncEnumerable<string> innerStream,
            LlmStreamingResponse streamingResponse)
        {
            await foreach (var chunk in innerStream)
            {
                yield return chunk;
            }

            var usage = streamingResponse.Usage ?? streamingResponse.FinalResponse?.Usage;
            await RecordUsageFromResponse(usage);
        }

        private async Task RecordUsageFromResponse(TokenUsage? usage)
        {
            if (usage == null || (usage.PromptTokens == 0 && usage.CompletionTokens == 0))
                return;

            _rateLimiter?.RecordRequest(Name, usage.TotalTokens);

            if (_costTracker != null)
            {
                try
                {
                    await _costTracker.RecordUsageAsync(new UsageRecord(
                        Provider: Name,
                        Model: "",
                        PromptTokens: usage.PromptTokens,
                        CompletionTokens: usage.CompletionTokens,
                        ProjectId: ProjectId,
                        TaskId: TaskId,
                        AgentType: AgentType,
                        CallerContext: CallerContext));
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to record usage for {Provider}", Name);
                }
            }
        }

        private static async IAsyncEnumerable<string> EmptyStream()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
