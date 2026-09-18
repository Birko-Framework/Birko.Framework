namespace Birko.AI.Providers
{
    /// <summary>
    /// Provider for DeepSeek models (OpenAI-compatible API).
    /// Default endpoint: https://api.deepseek.com/v1/chat/completions
    /// </summary>
    public class DeepSeekProvider : OpenAiCompatibleProviderBase
    {
        protected override string ProviderName => "DeepSeek";

        public DeepSeekProvider(string apiKey, string model = "deepseek-chat", string? baseUrl = null)
            : base(model, baseUrl ?? "https://api.deepseek.com/v1/chat/completions", apiKey)
        {
        }
    }
}
