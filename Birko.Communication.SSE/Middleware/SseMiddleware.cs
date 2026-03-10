using System.Collections.Generic;
using System.Threading.Tasks;

namespace Birko.Communication.SSE.Middleware
{
    /// <summary>
    /// Delegate for SSE request handling
    /// </summary>
    public delegate Task<SseResponse?> SseRequestDelegate(SseContext context);

    /// <summary>
    /// SSE request context passed through middleware pipeline
    /// </summary>
    public class SseContext
    {
        /// <summary>
        /// Gets or sets the request headers
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>
        /// Gets or sets the query string
        /// </summary>
        public string? QueryString { get; set; }

        /// <summary>
        /// Gets or sets the remote endpoint (IP:port)
        /// </summary>
        public string? RemoteEndPoint { get; set; }

        /// <summary>
        /// Gets or sets the authenticated client ID
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// Gets or sets additional items for passing data between middleware
        /// </summary>
        public Dictionary<string, object> Items { get; set; } = new();
    }

    /// <summary>
    /// SSE response from middleware
    /// </summary>
    public class SseResponse
    {
        /// <summary>
        /// Gets or sets whether the connection should be allowed
        /// </summary>
        public bool AllowConnection { get; set; }

        /// <summary>
        /// Gets or sets the HTTP status code
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Gets or sets the content type
        /// </summary>
        public string? ContentType { get; set; } = "text/event-stream";

        /// <summary>
        /// Gets or sets additional headers to send in the response
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>
        /// Creates a response that allows the connection
        /// </summary>
        public static SseResponse Allowed(Dictionary<string, string>? headers = null)
        {
            return new SseResponse
            {
                AllowConnection = true,
                Headers = headers ?? new Dictionary<string, string>()
            };
        }

        /// <summary>
        /// Creates a response that denies the connection
        /// </summary>
        public static SseResponse Denied(int statusCode = 401, string? reason = null)
        {
            return new SseResponse
            {
                AllowConnection = false,
                StatusCode = statusCode
            };
        }
    }

    /// <summary>
    /// Base interface for SSE middleware
    /// </summary>
    public interface ISseMiddleware
    {
        /// <summary>
        /// Processes the SSE connection request through this middleware
        /// </summary>
        Task<SseResponse?> ProcessAsync(SseContext context, SseRequestDelegate next);
    }
}
