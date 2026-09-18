namespace Birko.AI.Models
{
    /// <summary>
    /// Token usage data from an LLM API call.
    /// </summary>
    public class TokenUsage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens => PromptTokens + CompletionTokens;

        /// <summary>
        /// The model that produced this usage, as reported by the provider's response
        /// (e.g. "claude-opus", "gpt-4o"). Enables per-model cost tracking (CR-H007).
        /// Null when the provider response does not carry a model name.
        /// </summary>
        public string? Model { get; set; }
    }
}
