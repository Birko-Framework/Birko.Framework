namespace Birko.AI.Providers
{
    /// <summary>
    /// Provider for OpenRouter unified LLM gateway (OpenAI-compatible API).
    /// Supports 100+ models from multiple providers via a single API.
    /// Default endpoint: https://openrouter.ai/api/v1/chat/completions
    /// </summary>
    public class OpenRouterProvider : OpenAiCompatibleProviderBase
    {
        protected override string ProviderName => "OpenRouter";

        public OpenRouterProvider(string apiKey, string model = "anthropic/claude-3.5-sonnet", string? baseUrl = null)
            : base(model, baseUrl ?? "https://openrouter.ai/api/v1/chat/completions", apiKey)
        {
        }
    }
}
