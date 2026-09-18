namespace Birko.AI.Providers
{
    /// <summary>
    /// Provider for Mistral AI models (OpenAI-compatible API).
    /// Default endpoint: https://api.mistral.ai/v1/chat/completions
    /// </summary>
    public class MistralProvider : OpenAiCompatibleProviderBase
    {
        protected override string ProviderName => "Mistral";

        public MistralProvider(string apiKey, string model = "mistral-large-latest", string? baseUrl = null)
            : base(model, baseUrl ?? "https://api.mistral.ai/v1/chat/completions", apiKey)
        {
        }
    }
}
