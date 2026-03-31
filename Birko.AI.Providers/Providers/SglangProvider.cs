namespace Birko.AI.Providers
{
    /// <summary>
    /// Provider for SGLang server with OpenAI-compatible API.
    /// Default endpoint: http://localhost:30000
    /// </summary>
    public class SglangProvider : OpenAiCompatibleProviderBase
    {
        protected override string ProviderName => "SGLang";

        public SglangProvider(string model = "default", string baseUrl = "http://localhost:30000", string? apiKey = null)
            : base(model, baseUrl, apiKey)
        {
        }
    }
}
