using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Birko.Security.Authentication;

namespace Birko.Communication.WebSocket.Services
{
    /// <summary>
    /// WebSocket-specific authentication adapter for ASP.NET Core
    /// </summary>
    public class WebSocketAuthenticationService
    {
        private readonly AuthenticationService _authService;
        private readonly ILogger<WebSocketAuthenticationService> _logger;

        /// <summary>
        /// Initializes a new instance of the WebSocketAuthenticationService class
        /// </summary>
        /// <param name="config">The authentication configuration</param>
        /// <param name="logger">ASP.NET Core logger for this service</param>
        /// <param name="authLogger">ASP.NET Core logger for the authentication service</param>
        public WebSocketAuthenticationService(
            IOptions<WebSocketAuthenticationConfiguration> config,
            ILogger<WebSocketAuthenticationService> logger,
            ILogger<AuthenticationService> authLogger)
        {
            var authConfig = config.Value ?? new WebSocketAuthenticationConfiguration();
            _authService = new AuthenticationService(authConfig, authLogger);
            _logger = logger;
        }

        /// <summary>
        /// Checks if authentication is enabled
        /// </summary>
        public bool IsAuthenticationEnabled() => _authService.IsAuthenticationEnabled();

        /// <summary>
        /// Validates a token
        /// </summary>
        /// <param name="token">The token to validate</param>
        /// <param name="clientIp">The client IP address</param>
        /// <returns>True if valid; otherwise, false</returns>
        public bool ValidateToken(string? token, string? clientIp) => _authService.ValidateToken(token, clientIp);

        /// <summary>
        /// Extracts the authentication token from the query string
        /// </summary>
        /// <param name="context">The HTTP context</param>
        /// <returns>The extracted token or null</returns>
        public string? ExtractTokenFromQuery(HttpContext context)
        {
            return context.Request.Query["token"].FirstOrDefault();
        }

        /// <summary>
        /// Extracts the client IP address from the HTTP context
        /// </summary>
        /// <param name="context">The HTTP context</param>
        /// <returns>The client IP address or null</returns>
        public string? GetClientIpAddress(HttpContext context)
        {
            return AuthenticationService.GetClientIpAddress(
                headerName => context.Request.Headers[headerName].FirstOrDefault(),
                context.Connection.RemoteIpAddress?.ToString()
            );
        }

        /// <summary>
        /// Disposes the service
        /// </summary>
        public void Dispose() => _authService.Dispose();
    }
}
