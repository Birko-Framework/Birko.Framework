using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Birko.Communication.SSE.Middleware
{
    /// <summary>
    /// CORS middleware for SSE connections
    /// </summary>
    public class SseCorsMiddleware : ISseMiddleware
    {
        private readonly ILogger<SseCorsMiddleware> _logger;
        private readonly HashSet<string> _allowedOrigins;
        private readonly bool _allowAnyOrigin;

        /// <summary>
        /// Initializes a new instance of the SseCorsMiddleware class
        /// </summary>
        public SseCorsMiddleware(
            ILogger<SseCorsMiddleware> logger,
            IEnumerable<string>? allowedOrigins = null)
        {
            _logger = logger;
            _allowedOrigins = new HashSet<string>(allowedOrigins ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _allowAnyOrigin = _allowedOrigins.Contains("*");
        }

        /// <summary>
        /// Creates a new instance of the middleware
        /// </summary>
        public static SseCorsMiddleware Create(ILogger<SseCorsMiddleware> logger, IEnumerable<string>? allowedOrigins = null)
        {
            return new SseCorsMiddleware(logger, allowedOrigins);
        }

        /// <summary>
        /// Processes the SSE connection request with CORS handling
        /// </summary>
        public async Task<SseResponse?> ProcessAsync(SseContext context, SseRequestDelegate next)
        {
            var response = await next(context);

            if (response != null && context.Headers.TryGetValue("Origin", out var origin))
            {
                if (_allowAnyOrigin || _allowedOrigins.Contains(origin))
                {
                    response.Headers["Access-Control-Allow-Origin"] = origin;
                    response.Headers["Access-Control-Allow-Credentials"] = "true";
                    response.Headers["Access-Control-Allow-Headers"] = "Cache-Control, X-API-Key, Authorization, Last-Event-ID";
                }
                else
                {
                    _logger.LogWarning("CORS blocked origin: {Origin}", origin);
                    return SseResponse.Denied(403, "Origin not allowed");
                }
            }

            return response;
        }
    }
}
