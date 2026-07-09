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

        /// <summary>
        /// Maximum number of times to re-check the rate-limit window before giving up. Default: 10.
        /// </summary>
        public int MaxRateLimitWaitAttempts { get; set; } = 10;

        /// <summary>
        /// Cap on any single rate-limit wait, so a long day-window rollover can't block for hours.
        /// Default: 60 seconds.
        /// </summary>
        public TimeSpan MaxSingleRateLimitWait { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Waits for a rate-limit slot to actually open, re-checking after each delay rather than
        /// waiting once and proceeding blind (CR-M011). Returns <c>true</c> if a request may proceed,
        /// <c>false</c> if the limit still could not be satisfied within the attempt cap.
        /// </summary>
        private async Task<bool> WaitForRateLimitSlotAsync(string context, CancellationToken cancellationToken)
        {
            if (_rateLimiter == null)
                return true;

            for (var attempt = 0; attempt < MaxRateLimitWaitAttempts; attempt++)
            {
                if (_rateLimiter.CanMakeRequest(Name))
                    return true;

                var retryAfter = _rateLimiter.GetRetryAfter(Name);
                var delay = retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
                    ? retryAfter.Value
                    : TimeSpan.FromSeconds(1);
                if (delay > MaxSingleRateLimitWait)
                    delay = MaxSingleRateLimitWait;

                _logger?.LogWarning("Rate limited for {Provider} ({Context}), waiting {Seconds:F1}s (attempt {Attempt}/{Max})",
                    Name, context, delay.TotalSeconds, attempt + 1, MaxRateLimitWaitAttempts);
                await Task.Delay(delay, cancellationToken);
            }

            // Final re-check after exhausting the attempt cap.
            return _rateLimiter.CanMakeRequest(Name);
        }

        public async Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (!await WaitForRateLimitSlotAsync("sync", cancellationToken))
            {
                var limitMsg = $"Rate limit for {Name} could not be satisfied after waiting.";
                _logger?.LogWarning(limitMsg);
                return LlmResponse.Error(limitMsg);
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

            var response = await _inner.SendMessageAsync(messages, tools, systemPrompt, cancellationToken);
            await RecordUsageFromResponse(response.Usage);
            return response;
        }

        public async Task<LlmStreamingResponse> SendMessageStreamingAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
        {
            if (!await WaitForRateLimitSlotAsync("streaming", cancellationToken))
            {
                var limitMsg = $"Rate limit for {Name} could not be satisfied after waiting.";
                _logger?.LogWarning(limitMsg);
                return new LlmStreamingResponse
                {
                    GetStreamAsync = () => Task.FromResult<IAsyncEnumerable<string>>(EmptyStream()),
                    Error = limitMsg,
                    FinalResponse = LlmResponse.Error(limitMsg)
                };
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

            var streamingResponse = await _inner.SendMessageStreamingAsync(messages, tools, systemPrompt, cancellationToken);

            // Preserve the inner response's disposable Resource by re-wrapping only the stream delegate.
            var originalGetStream = streamingResponse.GetStreamAsync;
            streamingResponse.GetStreamAsync = async () =>
            {
                var stream = await originalGetStream();
                return WrapStreamForUsageTracking(stream, streamingResponse, cancellationToken);
            };

            return streamingResponse;
        }

        private async IAsyncEnumerable<string> WrapStreamForUsageTracking(
            IAsyncEnumerable<string> innerStream,
            LlmStreamingResponse streamingResponse,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in innerStream.WithCancellation(cancellationToken))
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
                        Model: usage.Model ?? "",
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
