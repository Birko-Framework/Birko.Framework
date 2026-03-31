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
    }
}
