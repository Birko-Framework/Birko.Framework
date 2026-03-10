using System.Collections.Generic;
using Birko.Security.Authentication;

namespace Birko.Communication.SSE.Middleware
{
    /// <summary>
    /// Configuration for SSE authentication
    /// </summary>
    public class SseAuthenticationConfiguration : AuthenticationConfiguration
    {
        /// <summary>
        /// Gets or sets the header name for API key authentication (default: X-API-Key)
        /// </summary>
        public string ApiKeyHeader { get; set; } = "X-API-Key";

        /// <summary>
        /// Gets or sets the prefix for the Authorization header (default: Bearer)
        /// </summary>
        public string AuthorizationHeaderPrefix { get; set; } = "Bearer";

        /// <summary>
        /// Gets or sets whether to allow tokens via query parameter (default: true)
        /// </summary>
        public bool AllowQueryToken { get; set; } = true;

        /// <summary>
        /// Gets or sets the query parameter name for tokens (default: "token")
        /// </summary>
        public string QueryTokenName { get; set; } = "token";
    }
}
