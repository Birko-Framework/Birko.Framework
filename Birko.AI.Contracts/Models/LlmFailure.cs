namespace Birko.AI.Models
{
    /// <summary>
    /// Why an LLM call failed, as the provider knows it (TASK-531). Classified once, in <c>LlmProviderBase</c>, from the
    /// HTTP status, the Retry-After header and the exception type — never from the error text.
    /// </summary>
    /// <remarks>
    /// ⚠ The numeric values are <b>pinned</b>: a kind stored as an integer reads back as the same kind.
    /// </remarks>
    public enum LlmFailureKind
    {
        /// <summary>Nothing the provider could classify. Permanent: retrying an unknown failure is a guess.</summary>
        Unknown = 0,
        /// <summary>429 rate limit. Transient; <see cref="LlmFailure.RetryAfter"/> says when, if the server said.</summary>
        RateLimited = 1,
        /// <summary>5xx. Transient.</summary>
        ServerError = 2,
        /// <summary>The request timed out (408, or the client's timeout). Transient.</summary>
        Timeout = 3,
        /// <summary>The connection failed before a response. Transient.</summary>
        Network = 4,
        /// <summary>400 and other 4xx the request itself caused. Permanent.</summary>
        BadRequest = 5,
        /// <summary>401: the key is missing or wrong. Permanent.</summary>
        Unauthorized = 6,
        /// <summary>402, or a quota the account has used up (OpenAI's 429 <c>insufficient_quota</c>). Permanent until someone pays.</summary>
        Billing = 7,
        /// <summary>403. Permanent.</summary>
        Forbidden = 8,
        /// <summary>404: usually a wrong model name or endpoint. Permanent.</summary>
        NotFound = 9,
        /// <summary>The provider has no key or model configured; no request was sent. Permanent.</summary>
        NotConfigured = 10,
        /// <summary>A success status with a body the provider could not read. Permanent.</summary>
        InvalidResponse = 11,
    }

    /// <summary>
    /// Whether a failed operation is worth retrying (TASK-531). ⚠ Pinned values, the same names and order as DraCode's
    /// <c>ErrorClassifier.ErrorCategory</c>, so a stored value of either reads back unchanged.
    /// </summary>
    public enum ErrorCategory
    {
        Transient = 0,
        Permanent = 1,
    }

    /// <summary>A classified LLM failure: the kind, the HTTP status when there was one, and the server's Retry-After.</summary>
    public sealed record LlmFailure(LlmFailureKind Kind, int? StatusCode = null, TimeSpan? RetryAfter = null)
    {
        /// <summary>True for a rate limit, a 5xx, a timeout or a network failure: the same request may succeed later.</summary>
        public bool IsTransient => Kind is LlmFailureKind.RateLimited or LlmFailureKind.ServerError
            or LlmFailureKind.Timeout or LlmFailureKind.Network;

        public ErrorCategory Category => IsTransient ? ErrorCategory.Transient : ErrorCategory.Permanent;

        public static LlmFailure Unknown { get; } = new(LlmFailureKind.Unknown);

        /// <summary>Classifies a non-success HTTP status.</summary>
        public static LlmFailure FromStatusCode(int statusCode, TimeSpan? retryAfter = null) => new(statusCode switch
        {
            429 => LlmFailureKind.RateLimited,
            408 => LlmFailureKind.Timeout,
            400 => LlmFailureKind.BadRequest,
            401 => LlmFailureKind.Unauthorized,
            402 => LlmFailureKind.Billing,
            403 => LlmFailureKind.Forbidden,
            404 => LlmFailureKind.NotFound,
            >= 500 => LlmFailureKind.ServerError,
            >= 400 => LlmFailureKind.BadRequest,
            _ => LlmFailureKind.Unknown,
        }, statusCode, retryAfter);
    }
}
