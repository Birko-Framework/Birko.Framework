namespace Birko.AI.Providers
{
    /// <summary>
    /// Provider for llama.cpp server with OpenAI-compatible API.
    /// Default endpoint: http://localhost:8080
    /// </summary>
    public class LlamaCppProvider : OpenAiCompatibleProviderBase
    {
        protected override string ProviderName => "llama.cpp";

        /// <summary>
        /// llama.cpp uses -1 for unlimited tokens
        /// </summary>
        protected override int MaxTokens => -1;

        public LlamaCppProvider(string model = "default", string baseUrl = "http://localhost:8080", string? apiKey = null)
            : base(model, baseUrl, apiKey)
        {
        }
    }
}
