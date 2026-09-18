using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Birko.Communication.SSE.Middleware
{
    /// <summary>
    /// Logging middleware for SSE connections
    /// </summary>
    public class SseLoggingMiddleware : ISseMiddleware
    {
        private readonly ILogger<SseLoggingMiddleware> _logger;

        /// <summary>
        /// Initializes a new instance of the SseLoggingMiddleware class
        /// </summary>
        public SseLoggingMiddleware(ILogger<SseLoggingMiddleware> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Creates a new instance of the middleware
        /// </summary>
        public static SseLoggingMiddleware Create(ILogger<SseLoggingMiddleware> logger)
        {
            return new SseLoggingMiddleware(logger);
        }

        /// <summary>
        /// Processes the SSE connection request with logging
        /// </summary>
        public async Task<SseResponse?> ProcessAsync(SseContext context, SseRequestDelegate next)
        {
            var clientId = context.ClientId ?? (context.Headers.ContainsKey("X-Request-ID") ? context.Headers["X-Request-ID"] : "unknown");
            var remoteIp = context.RemoteEndPoint ?? "unknown";

            _logger.LogInformation("SSE connection request from {RemoteIp} (Client: {ClientId})", remoteIp, clientId);

            var response = await next(context);

            if (response != null)
            {
                if (response.AllowConnection)
                {
                    _logger.LogInformation("SSE connection allowed for {ClientId}", clientId);
                }
                else
                {
                    _logger.LogWarning("SSE connection denied for {ClientId} - Status: {StatusCode}",
                        clientId, response.StatusCode);
                }
            }

            return response;
        }
    }
}
