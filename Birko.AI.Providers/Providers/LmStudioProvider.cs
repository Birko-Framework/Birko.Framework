namespace Birko.AI.Providers
{
    /// <summary>
    /// Provider for LM Studio local inference server (OpenAI-compatible API).
    /// Default endpoint: http://localhost:1234/v1/chat/completions
    /// No API key required for local access.
    /// </summary>
    public class LmStudioProvider : OpenAiCompatibleProviderBase
    {
        protected override string ProviderName => "LM Studio";

        public LmStudioProvider(string model = "local-model", string? baseUrl = null)
            : base(model, baseUrl ?? "http://localhost:1234/v1/chat/completions")
        {
        }
    }
}
