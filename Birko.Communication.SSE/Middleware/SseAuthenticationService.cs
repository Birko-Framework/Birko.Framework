using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Birko.Security.Authentication;

namespace Birko.Communication.SSE.Middleware
{
    /// <summary>
    /// Result of an authentication attempt for SSE connections
    /// </summary>
    public class SseAuthenticationResult
    {
        /// <summary>
        /// Gets or sets whether authentication was successful
        /// </summary>
        public bool IsAuthenticated { get; set; }

        /// <summary>
        /// Gets or sets the client ID (if authenticated)
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// Gets or sets the error message (if authentication failed)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the HTTP status code to return
        /// </summary>
        public int? StatusCode { get; set; }

        /// <summary>
        /// Creates a successful authentication result
        /// </summary>
        public static SseAuthenticationResult Success(string clientId)
        {
            return new SseAuthenticationResult
            {
                IsAuthenticated = true,
                ClientId = clientId
            };
        }

        /// <summary>
        /// Creates a failed authentication result
        /// </summary>
        public static SseAuthenticationResult Fail(string errorMessage, int statusCode = 401)
        {
            return new SseAuthenticationResult
            {
                IsAuthenticated = false,
                ErrorMessage = errorMessage,
                StatusCode = statusCode
            };
        }
    }

    /// <summary>
    /// Service for authenticating SSE connections
    /// </summary>
    public class SseAuthenticationService : IDisposable
    {
        private readonly AuthenticationService _authService;
        private readonly SseAuthenticationConfiguration _config;
        private readonly ILogger<SseAuthenticationService>? _logger;

        /// <summary>
        /// Initializes a new instance of the SseAuthenticationService class
        /// </summary>
        public SseAuthenticationService(
            SseAuthenticationConfiguration config,
            ILogger<SseAuthenticationService>? logger = null,
            ILogger<AuthenticationService>? authLogger = null)
        {
            _config = config ?? new SseAuthenticationConfiguration();
            _logger = logger;
            _authService = new AuthenticationService(_config, authLogger);
        }

        /// <summary>
        /// Checks if authentication is enabled
        /// </summary>
        public bool IsAuthenticationEnabled() => _authService.IsAuthenticationEnabled();

        /// <summary>
        /// Authenticates an SSE connection request
        /// </summary>
        public SseAuthenticationResult AuthenticateConnection(
            Dictionary<string, string>? headers,
            string? queryString,
            string? remoteEndPoint)
        {
            if (!_authService.IsAuthenticationEnabled())
            {
                _logger?.LogDebug("Authentication is disabled, allowing connection");
                return SseAuthenticationResult.Success(Guid.NewGuid().ToString("N"));
            }

            var token = ExtractToken(headers, queryString);
            var clientIp = GetClientIpAddress(headers, remoteEndPoint);

            if (string.IsNullOrEmpty(token))
            {
                _logger?.LogWarning("Authentication failed: No token found from {ClientIp}", clientIp);
                return SseAuthenticationResult.Fail("Authentication required: valid token not found");
            }

            if (_authService.ValidateToken(token, clientIp))
            {
                _logger?.LogInformation("Token authenticated for {ClientIp}", clientIp);
                return SseAuthenticationResult.Success(Guid.NewGuid().ToString("N"));
            }

            _logger?.LogWarning("Authentication failed: Invalid token from {ClientIp}", clientIp);
            return SseAuthenticationResult.Fail("Invalid authentication token");
        }

        /// <summary>
        /// Extracts the authentication token from headers or query string
        /// </summary>
        private string? ExtractToken(Dictionary<string, string>? headers, string? queryString)
        {
            // Try API Key header first
            if (headers != null && headers.TryGetValue(_config.ApiKeyHeader, out var apiKey))
            {
                return apiKey;
            }

            // Try Authorization header
            if (headers != null && headers.TryGetValue("Authorization", out var authHeader))
            {
                return ExtractTokenFromAuthorizationHeader(authHeader);
            }

            // Try query parameter
            if (_config.AllowQueryToken && !string.IsNullOrEmpty(queryString))
            {
                return ExtractTokenFromQueryString(queryString);
            }

            return null;
        }

        /// <summary>
        /// Extracts token from Authorization header
        /// </summary>
        private string? ExtractTokenFromAuthorizationHeader(string authHeader)
        {
            if (authHeader.StartsWith($"{_config.AuthorizationHeaderPrefix} ", StringComparison.OrdinalIgnoreCase))
            {
                return authHeader.Substring(_config.AuthorizationHeaderPrefix.Length + 1);
            }
            return null;
        }

        /// <summary>
        /// Extracts token from query string
        /// </summary>
        private string? ExtractTokenFromQueryString(string queryString)
        {
            var pairs = queryString.Split('&');
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=');
                if (parts.Length == 2 &&
                    string.Equals(parts[0], _config.QueryTokenName, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the client IP address from headers or remote endpoint
        /// </summary>
        private string? GetClientIpAddress(Dictionary<string, string>? headers, string? remoteEndPoint)
        {
            return AuthenticationService.GetClientIpAddress(
                headers != null ? (key => headers.TryGetValue(key, out var value) ? value : null) : null,
                remoteEndPoint
            );
        }

        /// <summary>
        /// Validates a token
        /// </summary>
        public bool ValidateToken(string? token, string? remoteEndPoint)
        {
            return _authService.ValidateToken(token, remoteEndPoint);
        }

        public void Dispose()
        {
            _authService.Dispose();
        }
    }
}
