using System;
using Microsoft.Extensions.Logging;
using Birko.Security.Authentication;

namespace Birko.Communication.REST.Middleware
{
    /// <summary>
    /// REST-specific authentication adapter that extracts tokens from REST requests
    /// </summary>
    public class RestAuthenticationService
    {
        private readonly AuthenticationService _authService;
        private readonly RestAuthenticationConfiguration _config;
        private readonly ILogger<RestAuthenticationService>? _logger;

        /// <summary>
        /// Initializes a new instance of the RestAuthenticationService class
        /// </summary>
        /// <param name="config">The REST authentication configuration</param>
        /// <param name="logger">Logger for this service</param>
        /// <param name="authLogger">Logger for the authentication service</param>
        public RestAuthenticationService(
            RestAuthenticationConfiguration config,
            ILogger<RestAuthenticationService>? logger = null,
            ILogger<AuthenticationService>? authLogger = null)
        {
            _config = config ?? new RestAuthenticationConfiguration();
            _logger = logger;
            _authService = new AuthenticationService(_config, authLogger);
        }

        /// <summary>
        /// Checks if authentication is enabled
        /// </summary>
        public bool IsAuthenticationEnabled() => _authService.IsAuthenticationEnabled();

        /// <summary>
        /// Validates a token extracted from the request
        /// </summary>
        /// <param name="token">The token to validate</param>
        /// <param name="clientIp">The client IP address</param>
        /// <returns>True if valid; otherwise, false</returns>
        public bool ValidateToken(string? token, string? clientIp) => _authService.ValidateToken(token, clientIp);

        /// <summary>
        /// Extracts the authentication token from a REST request
        /// </summary>
        /// <param name="request">The REST request</param>
        /// <returns>The extracted token or null</returns>
        public string? ExtractTokenFromRequest(RestRequest request)
        {
            // Try Authorization header first (Bearer token)
            if (!string.IsNullOrEmpty(_config.AuthorizationHeader))
            {
                var authHeader = request.GetHeader(_config.AuthorizationHeader);
                if (!string.IsNullOrEmpty(authHeader))
                {
                    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        return authHeader.Substring(7);
                    }
                    return authHeader;
                }
            }

            // Try API Key header
            if (!string.IsNullOrEmpty(_config.ApiKeyHeader))
            {
                var apiKey = request.GetHeader(_config.ApiKeyHeader);
                if (!string.IsNullOrEmpty(apiKey))
                {
                    return apiKey;
                }
            }

            // Try query parameter
            if (_config.AllowQueryToken && !string.IsNullOrEmpty(_config.QueryTokenName))
            {
                return request.GetQueryParameter(_config.QueryTokenName);
            }

            return null;
        }

        /// <summary>
        /// Extracts the client IP address from a REST request
        /// </summary>
        /// <param name="request">The REST request</param>
        /// <returns>The client IP address or null</returns>
        public string? GetClientIpAddress(RestRequest request)
        {
            return AuthenticationService.GetClientIpAddress(
                request.GetHeader,
                request.UserHostAddress
            );
        }

        /// <summary>
        /// Disposes the service
        /// </summary>
        public void Dispose() => _authService.Dispose();
    }
}
