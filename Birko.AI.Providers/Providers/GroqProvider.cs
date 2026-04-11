namespace Birko.AI.Providers
{
    /// <summary>
    /// Provider for Groq fast inference (OpenAI-compatible API).
    /// Default endpoint: https://api.groq.com/openai/v1/chat/completions
    /// </summary>
    public class GroqProvider : OpenAiCompatibleProviderBase
    {
        protected override string ProviderName => "Groq";

        public GroqProvider(string apiKey, string model = "llama-3.3-70b-versatile", string? baseUrl = null)
            : base(model, baseUrl ?? "https://api.groq.com/openai/v1/chat/completions", apiKey)
        {
        }
    }
}
