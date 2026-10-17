namespace Birko.AI.Models
{
    public class LlmResponse
    {
        public string? StopReason { get; set; }
        public List<ContentBlock>? Content { get; set; }

        /// <summary>
        /// Token usage data from the API response (prompt + completion tokens).
        /// </summary>
        public TokenUsage? Usage { get; set; }

        /// <summary>
        /// Detailed error message from the LLM provider when StopReason is "error".
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Why the call failed, when it did (TASK-531): set by <see cref="Error"/> and by the providers' not-configured
        /// response, <see cref="LlmFailure.Unknown"/> when the provider could not classify it. <c>null</c> on success.
        /// </summary>
        public LlmFailure? Failure { get; set; }

        /// <summary>True when the call failed and the same request may succeed later. See <see cref="LlmFailure.IsTransient"/>.</summary>
        public bool IsTransient => Failure?.IsTransient ?? false;

        /// <summary>The HTTP status of a failed call, when there was a response.</summary>
        public int? StatusCode => Failure?.StatusCode;

        /// <summary>How long the server asked the caller to wait, when it said.</summary>
        public TimeSpan? RetryAfter => Failure?.RetryAfter;

        /// <summary>
        /// Creates an error LlmResponse with the given error message. Without a <paramref name="failure"/> it is
        /// <see cref="LlmFailure.Unknown"/>, which is permanent.
        /// </summary>
        public static LlmResponse Error(string? errorMessage = null, LlmFailure? failure = null) => new()
        {
            StopReason = "error",
            Content = [],
            ErrorMessage = errorMessage,
            Failure = failure ?? LlmFailure.Unknown,
        };
    }
}
