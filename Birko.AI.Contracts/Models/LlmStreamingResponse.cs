namespace Birko.AI.Models
{
    /// <summary>
    /// Response from LLM provider for streaming requests.
    /// Supports both text streaming and tool call capture.
    /// Disposing it releases the underlying transport resource (e.g. the HTTP response) so an
    /// abandoned stream — one whose enumeration is stopped early by an exception or cancellation —
    /// does not leak the connection (CR-M003).
    /// </summary>
    public class LlmStreamingResponse : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Async enumerable stream of response chunks (text only).
        /// Call this to consume the stream, then check FinalResponse for tool calls.
        /// </summary>
        public required Func<Task<IAsyncEnumerable<string>>> GetStreamAsync { get; set; }

        /// <summary>
        /// Stop reason if known upfront (usually null for streaming, populated after stream completes).
        /// </summary>
        public string? StopReason { get; set; }

        /// <summary>
        /// Error message if streaming failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Whether the stream completed successfully.
        /// </summary>
        public bool IsComplete { get; set; }

        /// <summary>
        /// Full response after streaming completes, including any tool calls.
        /// Populated by the provider during streaming and available after GetStreamAsync completes.
        /// </summary>
        public LlmResponse? FinalResponse { get; set; }

        /// <summary>
        /// Accumulated text content from streaming (for convenience).
        /// Populated during streaming by providers that support it.
        /// </summary>
        public string? AccumulatedText { get; set; }

        /// <summary>
        /// Token usage data from the streaming response (populated after stream completes).
        /// </summary>
        public TokenUsage? Usage { get; set; }

        /// <summary>
        /// Underlying transport resource (typically the streaming <c>HttpResponseMessage</c>) that
        /// owns the network connection. The provider assigns it when it opens the stream; disposing
        /// this response disposes it, closing the connection even if the stream is abandoned early.
        /// </summary>
        public IDisposable? Resource { get; set; }

        public void Dispose()
        {
            Resource?.Dispose();
            Resource = null;
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
